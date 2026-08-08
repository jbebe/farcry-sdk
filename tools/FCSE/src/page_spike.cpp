#include "page_spike.h"

#include "dunia_api.h"
#include "ini_file.h"
#include "log.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <windows.h>

// Dunia.dll (Steam v1.03) RVAs, same base-plus-RVA convention mod_page.cpp uses. Every address and
// offset below was confirmed by decompile and then exercised in-game; the trail is in
// tools/FCSE/PLAN-own-page.md and in decompiler comments in the shared Ghidra project.
namespace FCSE {

namespace {
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;

    // CFCXOptionGamePage's real ctor, and the allocation size AddPage<CFCXOptionGamePage> uses.
    //
    // Deliberately a *concrete leaf*, not the tidier-looking CFCXBaseOptionPage one class below it
    // (0x1087ec80). That base was tried on 2026-08-08 and is **abstract**: constructing it works and
    // the page displays correctly, but pressing Back kills the process with R6025 "pure virtual
    // function call" - the input path invokes one of its pure virtuals before dispatch ever reaches
    // a menu handler (confirmed: the instrumented handler vtable logged nothing at all on that
    // press). Its content-build slot at vtable+0x3c being a plain RET says nothing about the rest of
    // the table; do not read that as "concrete" again.
    //
    // The cost of using the leaf is that CFCXOptionGamePage::RefreshOptionList runs on this page
    // too - it rebuilds the Game tab's eight settings rows, which are then cleared and replaced (see
    // mod_page.cpp's RebuildRows). Wasteful but invisible, and Back works.
    constexpr uintptr_t kGamePageCtorRva = 0x1081e9c0;
    constexpr size_t kPageSize = 0x210;

    // CUIPageBase::Init - the call that turns the page's authored name into a bound magma::Page and
    // its widgets. Nothing in the engine calls it implicitly, which is why every earlier hand-built
    // page displayed nothing and then faulted. __fastcall, one param (this in ECX).
    constexpr uintptr_t kUiPageBaseInitRva = 0x10109410;

    constexpr uintptr_t kAddButtonRva = 0x10cdbb80;
    constexpr uintptr_t kSwitchPageRva = 0x101d1990;

    // magma::TextBase::SetText - takes a RAW wchar_t*, so no string object has to be forged. A
    // byte-for-byte behavioural match to FarCry2_server's CMenuPage::SetTitle tail.
    constexpr uintptr_t kTextBaseSetTextRva = 0x1007d770;

    // CUIPageBase's page-name std::string. Note the object actually starts at +0x28 with its
    // embedded allocator; these three are the fields Init itself reads, and overwriting only them
    // leaves the ctor's allocator in place. Init branches on `capacity < 0x10` to decide whether the
    // characters live inline at +0x2c or behind a pointer there.
    constexpr ptrdiff_t kPageNameDataOffset = 0x2c;
    constexpr ptrdiff_t kPageNameSizeOffset = 0x3c;
    constexpr ptrdiff_t kPageNameCapacityOffset = 0x40;
    constexpr size_t kNarrowSsoCapacity = 15;

    // CMenuPage's stored title std::wstring (object starts at +0xf0 with its allocator). SSO holds
    // 7 wchar_t in the same 16-byte buffer.
    constexpr ptrdiff_t kTitleDataOffset = 0xf4;
    constexpr ptrdiff_t kTitleSizeOffset = 0x104;
    constexpr ptrdiff_t kTitleCapacityOffset = 0x108;
    constexpr size_t kWideSsoCapacity = 7;

    constexpr wchar_t kTitle[] = L"FCSE Settings";

    constexpr ptrdiff_t kBoundMagmaPageOffset = 0x14; // written by SetPage
    constexpr ptrdiff_t kRowListElementOffset = 0x08; // written by FetchMagmaElements
    constexpr ptrdiff_t kRowListBoxOffset = 0x0c;     //   "
    constexpr ptrdiff_t kTitleTextOffset = 0x10;      //   "
    constexpr ptrdiff_t kInitedFlagOffset = 0x68;     // set to 1 at the end of Init

    // What AddPage<T>'s real caller passes as its second argument and AddPage<T> stores here.
    constexpr ptrdiff_t kParentPageOffset = 0xec;

    // The owning CGameMenu*, read by CSetNextPageMenuHandler::SwitchPage itself.
    constexpr ptrdiff_t kOwnerPageToGameMenuOffset = 0x140;

    // CGameMenu's "next page" field. SwitchPage reads it, deactivates +0x40 (current) and activates
    // this one - it never touches the page hashtable, which is why this route avoids the
    // InsertNode crash entirely.
    constexpr ptrdiff_t kGameMenuNextPageOffset = 0x3c;

    using GamePageCtorFn = void(__thiscall*)(void* thisPtr);
    using InitFn = void(__thiscall*)(void* thisPtr);
    using AddButtonFn = void*(__thiscall*)(void* thisPtr, const wchar_t* label, char visible,
                                            void* handler);
    using SwitchPageFn = void(__thiscall*)(void* gameMenuThis);
    using SetTextFn = void(__thiscall*)(void* textBase, const wchar_t* text);

    GamePageCtorFn g_gamePageCtor = nullptr;
    InitFn g_init = nullptr;
    AddButtonFn g_addButton = nullptr;
    SwitchPageFn g_switchPage = nullptr;
    SetTextFn g_textBaseSetText = nullptr;

    void* g_spikePage = nullptr;

    // Each SEH wrapper holds exactly one native touchpoint and no C++ object with a destructor -
    // MSVC forbids mixing __try/__except with automatic unwinding in one function. Same convention
    // mod_page.cpp already follows.

    bool SafeCall(void(__thiscall* fn)(void*), void* thisPtr, DWORD* outCode) {
        __try {
            fn(thisPtr);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeReadPointer(void* base, ptrdiff_t offset, void** outValue, DWORD* outCode) {
        __try {
            *outValue = *reinterpret_cast<void**>(reinterpret_cast<char*>(base) + offset);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeWritePointer(void* base, ptrdiff_t offset, void* value, DWORD* outCode) {
        __try {
            *reinterpret_cast<void**>(reinterpret_cast<char*>(base) + offset) = value;
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeSetText(void* textBase, const wchar_t* text, DWORD* outCode) {
        __try {
            g_textBaseSetText(textBase, text);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeAddButton(void* thisPtr, const wchar_t* label, void* handler, DWORD* outCode) {
        __try {
            g_addButton(thisPtr, label, 1, handler);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    void LogFailed(const char* what, DWORD code) {
        char buf[160];
        std::snprintf(buf, sizeof(buf), "PageSpike: %s raised SEH exception 0x%08lX (caught)", what,
                      static_cast<unsigned long>(code));
        Log::Loader(buf);
    }

    void LogPointer(const char* what, void* page, ptrdiff_t offset) {
        DWORD code = 0;
        void* value = nullptr;
        if (!SafeReadPointer(page, offset, &value, &code)) {
            LogFailed(what, code);
            return;
        }
        char buf[160];
        std::snprintf(buf, sizeof(buf), "PageSpike:   %s (+0x%02X) = 0x%08X", what,
                      static_cast<unsigned>(offset), reinterpret_cast<unsigned>(value));
        Log::Loader(buf);
    }

    // Overwrites one of the page's own MSVC std::string/wstring members in place, leaving the
    // allocator the ctor already stored. Long values get a deliberately leaked buffer: the page is
    // never destroyed, so nothing runs the string's destructor, and handing the engine's allocator
    // a buffer it did not allocate would be the worse failure.
    template <typename Ch, size_t SsoCapacity>
    void OverwriteString(void* page, ptrdiff_t dataOffset, ptrdiff_t sizeOffset,
                          ptrdiff_t capacityOffset, const Ch* text, size_t length) {
        char* base = reinterpret_cast<char*>(page);
        if (length <= SsoCapacity) {
            std::memcpy(base + dataOffset, text, (length + 1) * sizeof(Ch));
            *reinterpret_cast<uint32_t*>(base + capacityOffset) = SsoCapacity;
        } else {
            Ch* buffer = new Ch[length + 1];
            std::memcpy(buffer, text, (length + 1) * sizeof(Ch));
            *reinterpret_cast<Ch**>(base + dataOffset) = buffer;
            *reinterpret_cast<uint32_t*>(base + capacityOffset) = static_cast<uint32_t>(length);
        }
        *reinterpret_cast<uint32_t*>(base + sizeOffset) = static_cast<uint32_t>(length);
    }

    // Both halves: the stored wstring is what anything re-applying the title later reads, and the
    // direct widget push is what makes it appear now. Only meaningful after Init(), which is what
    // binds the title widget at +0x10 - the 2026-08-04 investigation that concluded the title was
    // unreachable scanned for it before any binding existed.
    void SetPageTitle(void* page, const wchar_t* title) {
        OverwriteString<wchar_t, kWideSsoCapacity>(page, kTitleDataOffset, kTitleSizeOffset,
                                                    kTitleCapacityOffset, title, std::wcslen(title));
        DWORD code = 0;
        void* titleText = nullptr;
        if (!SafeReadPointer(page, kTitleTextOffset, &titleText, &code) || titleText == nullptr) {
            Log::Loader("PageSpike: no title TextBase bound, stored the string only");
            return;
        }
        if (!SafeSetText(titleText, title, &code)) {
            LogFailed("magma::TextBase::SetText", code);
            return;
        }
        Log::Loader("PageSpike: title applied via magma::TextBase::SetText");
    }

    // Hand-rolled IMenuItemHandler: a struct whose first member is a vtable pointer, one real slot,
    // the rest safe no-ops. kActivateSlot = 1 is no longer a guess - an instrumented run on
    // 2026-08-08 logged slot 1 and only slot 1 for a row click, and slot 0 is independently known to
    // be the MSVC scalar deleting destructor (the engine's own handler vtables put 0x1081b420,
    // a "call base dtor, free if arg & 1" stub, there).
    struct SpikeNavigationHandler {
        void** vtable; // must stay first
        void* ownerPage;
        void* targetPage;

        unsigned int SafeNoOp(unsigned int /*arg*/) { return 0; }

        unsigned int OnActivate(unsigned int /*arg*/) {
            DWORD code = 0;
            void* gameMenu = nullptr;
            if (!SafeReadPointer(ownerPage, kOwnerPageToGameMenuOffset, &gameMenu, &code)) {
                LogFailed("reading ownerPage+0x140 at click time", code);
                return 0;
            }
            if (gameMenu == nullptr || targetPage == nullptr || g_switchPage == nullptr) {
                Log::Loader("PageSpike: no CGameMenu* or page unavailable, click ignored");
                return 0;
            }
            if (!SafeWritePointer(gameMenu, kGameMenuNextPageOffset, targetPage, &code)) {
                LogFailed("writing CGameMenu+0x3c", code);
                return 0;
            }
            Log::Loader("PageSpike: switching to the spike page");
            if (!SafeCall(reinterpret_cast<void(__thiscall*)(void*)>(g_switchPage), gameMenu,
                          &code)) {
                LogFailed("CGameMenu::SwitchPage", code);
                return 0;
            }
            Log::Loader("PageSpike: SwitchPage returned without faulting");
            return 0;
        }

        static SpikeNavigationHandler* Create(void* ownerPage, void* targetPage);
    };

    constexpr int kVtableSlotCount = 8;
    constexpr int kActivateSlot = 1;

    using SpikeHandlerMemberFn = unsigned int (SpikeNavigationHandler::*)(unsigned int);

    void* RawFunctionPointer(SpikeHandlerMemberFn fn) {
        union {
            SpikeHandlerMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_handlerVtable[kVtableSlotCount];
    bool g_handlerVtableReady = false;

    SpikeNavigationHandler* SpikeNavigationHandler::Create(void* ownerPage, void* targetPage) {
        if (!g_handlerVtableReady) {
            void* noOp = RawFunctionPointer(&SpikeNavigationHandler::SafeNoOp);
            for (void*& slot : g_handlerVtable) {
                slot = noOp;
            }
            g_handlerVtable[kActivateSlot] =
                RawFunctionPointer(&SpikeNavigationHandler::OnActivate);
            g_handlerVtableReady = true;
        }
        auto* handler = new SpikeNavigationHandler();
        handler->vtable = g_handlerVtable;
        handler->ownerPage = ownerPage;
        handler->targetPage = targetPage;
        return handler;
    }

    // Reads the knobs out of bin\fcse.ini directly rather than through SettingsRegistry: this is a
    // developer switch, not a player-facing setting, and it must not appear as a row in the Mod
    // Configuration Menu or get written back into a plugin's group.
    bool ReadConfig(std::string* outPageName) {
        IniFile ini;
        if (!ini.Load(Log::LoaderDirectory() + L"fcse.ini")) {
            return false;
        }
        const std::string* enabled = ini.Find("FCSE", "Page spike");
        if (enabled == nullptr || (*enabled != "true" && *enabled != "1")) {
            return false;
        }
        // Which already-shipped magma page to borrow, configurable so a different target can be
        // tried without a rebuild. Defaults to the Network tab's layout: it exists in every shipped
        // options.mgb and is the least-missed screen in single-player.
        const std::string* name = ini.Find("FCSE", "Page spike name");
        *outPageName = (name != nullptr && !name->empty()) ? *name : "MAINMENU_OPTION_NETWORK";
        return true;
    }
} // namespace

bool PageSpike::Install(void* optionsMenuThis) {
    std::string pageName;
    if (!ReadConfig(&pageName)) {
        return false;
    }

    uintptr_t base = DuniaApi::Base();
    if (base == 0 || optionsMenuThis == nullptr) {
        Log::Loader("PageSpike: Dunia.dll not resolved or no owner page, skipping");
        return false;
    }
    auto resolve = [base](uintptr_t rva) { return base + (rva - kDuniaPreferredBase); };
    g_gamePageCtor = reinterpret_cast<GamePageCtorFn>(resolve(kGamePageCtorRva));
    g_init = reinterpret_cast<InitFn>(resolve(kUiPageBaseInitRva));
    g_addButton = reinterpret_cast<AddButtonFn>(resolve(kAddButtonRva));
    g_switchPage = reinterpret_cast<SwitchPageFn>(resolve(kSwitchPageRva));
    g_textBaseSetText = reinterpret_cast<SetTextFn>(resolve(kTextBaseSetTextRva));

    Log::Loader("PageSpike: enabled, target magma page \"" + pageName + "\"");

    // Zero-initialized: CListMenuPage's own base-class fields (the row array and friends) are never
    // written by the ctor, and zero is what "empty row list" means.
    void* page = new unsigned char[kPageSize]();
    DWORD code = 0;
    if (!SafeCall(reinterpret_cast<void(__thiscall*)(void*)>(g_gamePageCtor), page, &code)) {
        LogFailed("CFCXOptionGamePage::CFCXOptionGamePage", code);
        return false;
    }
    Log::Loader("PageSpike: private CFCXOptionGamePage constructed");

    // Retarget the page at a different magma layout by overwriting the name the ctor stored. The
    // ctor's own allocator field at +0x28 is left untouched.
    OverwriteString<char, kNarrowSsoCapacity>(page, kPageNameDataOffset, kPageNameSizeOffset,
                                               kPageNameCapacityOffset, pageName.c_str(),
                                               pageName.size());

    void* gameMenu = nullptr;
    if (SafeReadPointer(optionsMenuThis, kOwnerPageToGameMenuOffset, &gameMenu, &code)) {
        SafeWritePointer(page, kOwnerPageToGameMenuOffset, gameMenu, &code);
    } else {
        LogFailed("reading ownerPage+0x140", code);
    }
    SafeWritePointer(page, kParentPageOffset, optionsMenuThis, &code);

    if (!SafeCall(reinterpret_cast<void(__thiscall*)(void*)>(g_init), page, &code)) {
        LogFailed("CUIPageBase::Init", code);
        return false;
    }
    Log::Loader("PageSpike: CUIPageBase::Init returned - bound state follows");
    LogPointer("magma::Page", page, kBoundMagmaPageOffset);
    LogPointer("row list Element", page, kRowListElementOffset);
    LogPointer("row ListBox", page, kRowListBoxOffset);
    LogPointer("title TextBase", page, kTitleTextOffset);
    LogPointer("inited flag", page, kInitedFlagOffset);

    void* boundPage = nullptr;
    if (SafeReadPointer(page, kBoundMagmaPageOffset, &boundPage, &code) && boundPage == nullptr) {
        Log::Loader("PageSpike: no magma::Page bound - the name did not resolve through any loaded "
                    "package's GenericObjectTable. Displaying it would show nothing; not adding the "
                    "Options row.");
        return false;
    }

    g_spikePage = page;
    SetPageTitle(page, kTitle);

    // Rows are NOT added here. CFCXOptionGamePage::RefreshOptionList clears the row list every time
    // the page is displayed, so anything appended now is wiped before the player sees it. AppendRows
    // is called from inside that rebuild instead - see mod_page.cpp's RebuildRows.

    SpikeNavigationHandler* handler = SpikeNavigationHandler::Create(optionsMenuThis, page);
    if (!SafeAddButton(optionsMenuThis, L"[spike] private page", handler, &code)) {
        LogFailed("AddButton (Options row)", code);
        return false;
    }
    Log::Loader("PageSpike: added \"[spike] private page\" row to Options");
    return true;
}

bool PageSpike::OwnsPage(void* page) { return g_spikePage != nullptr && page == g_spikePage; }

void PageSpike::AppendRows(void* page) {
    DWORD code = 0;
    // Re-applied per display: the native rebuild this runs inside may have reset the title along
    // with the rows, and it is cheap enough not to be worth finding out the hard way.
    SetPageTitle(page, kTitle);
    if (!SafeAddButton(page, L"FCSE page spike - it worked", nullptr, &code)) {
        LogFailed("AddButton (spike page row)", code);
        return;
    }
    if (!SafeAddButton(page, L"second row", nullptr, &code)) {
        LogFailed("AddButton (spike page row 2)", code);
        return;
    }
    Log::Loader("PageSpike: appended 2 row(s) from inside the per-display rebuild");
}

} // namespace FCSE
