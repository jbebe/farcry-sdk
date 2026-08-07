#include "mod_page.h"

#include "dunia_api.h"
#include "hook.h"
#include "log.h"
#include "menu_handler.h"
#include "plugin_loader.h"
#include "settings_registry.h"

#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>
#include <windows.h>

// All addresses below are Dunia.dll (Steam v1.03) RVAs, same base-plus-RVA convention
// mods_tab.cpp already uses. See mod_page.h for the overall approach and
// docs/docs/engine-internals/magma-menu-system.md for the full RE trail each address/offset below
// was confirmed from (GhidraMCP decompiles + disassembly against the live Dunia.dll project,
// sessions 2026-08-04).
namespace FCSE {

namespace {
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;

    // CListMenuPage::AddButton - confirmed safe on any CListMenuPage-derived `this` (the fields it
    // touches, +0xc/+0xd4/+0x168/+0x16c, belong to CListMenuPage's own base-class layout, identical
    // for every subclass instance, not overridden per leaf class). Already proven live on
    // CFCXOptionPage's own `this`.
    constexpr uintptr_t kAddButtonRva = 0x10cdbb80;

    // CGameMenu::SetNextPage - fully decompiled: runs the shared CGameMenu_PageTable_Find lookup
    // (this+0x14 miss sentinel, same as GetPage) and, on a hit, copies the found node's own value
    // (node+0xc - the real, already-constructed page pointer the engine's own boot sequence put
    // there) into this+0x3c ("next page"). Takes `(CGameMenu* this, uint32_t* key)` - a pointer to
    // the target CStringID, not by value (confirmed: the decompile passes its own param_2 straight
    // through to Find's own key parameter, which every other confirmed caller in this codebase
    // passes a pointer for).
    constexpr uintptr_t kSetNextPageRva = 0x101d1bc0;

    // CGameMenu::SwitchPage - fully decompiled: reads/writes this+0x3c ("next")/this+0x40
    // ("current") and calls two vtable slots (deactivate old, activate new) - the real transition.
    // __fastcall, one param (this in ECX).
    constexpr uintptr_t kSwitchPageRva = 0x101d1990;

    // CFCXOptionGamePage::RefreshOptionList - the real per-page content builder, found 2026-08-04
    // by hooking AddButton globally (logging every caller) after FCSE's rows appended right after
    // page construction turned out to have zero visible effect - traced one level up from the
    // native AddBoolSetting/AddValueListSetting calls it makes
    // (0x10cde0d0/0x1081d660) to this function via CGameMenu-shared xrefs. Unlike
    // CFCXOptionPage::Setup (fires once per session), this one is named "Refresh" for a reason: it
    // opens with a virtual call through `*this+0x40` (almost certainly a "clear my rows" call) and
    // rebuilds all ~9 native rows from scratch, every time the page is (re)displayed - including via
    // the stock "Game" button. Hooked once, globally, gated by g_appendPending (see below) rather
    // than by `this`, since both the stock "Game" button and FCSE's own button now land on the
    // exact same real page object. __fastcall, one param (this in ECX, no stack args).
    constexpr uintptr_t kRefreshOptionListRva = 0x10820160;

    // The existing, compiled-in Game tab's own CStringID (native CRC-32 of "CFCXOptionGamePage",
    // verified against the engine's CRC32_Hash algorithm).
    constexpr uint32_t kGameOptionsPageId = 0xe0d85c6e;

    // ownerPage+0x140 = the owning CGameMenu* - confirmed via CSetNextPageMenuHandler::SwitchPage's
    // own disassembly (0x10188d00), which reads this exact offset before calling GetPage/
    // SetNextPage/SwitchPage. optionsMenuThis is a valid ownerPage for the same reason it's already
    // a valid AddButton `this`.
    constexpr ptrdiff_t kOwnerPageToGameMenuOffset = 0x140;

    // The virtual slot RefreshOptionList itself calls first, unconditionally, before building any
    // native rows (`(**(code **)(*param_1 + 0x40))();` in its own decompile - see
    // kRefreshOptionListRva's comment). Never independently named/confirmed as "ClearRows" - only
    // inferred from its position (right before the row-building calls) - but it's the exact
    // mechanism the engine itself already trusts to reset the row list, so reusing it (called a
    // second time, after the native rows are built) to hide them for FCSE's own page view is lower-
    // risk than trying to zero/patch the row-array fields directly.
    constexpr ptrdiff_t kClearRowsVtableOffset = 0x40;

    using AddButtonFn = void*(__thiscall*)(void* thisPtr, const wchar_t* label, char visible,
                                            void* handler);
    using SetNextPageFn = void(__thiscall*)(void* gameMenuThis, uint32_t* key);
    using SwitchPageFn = void(__thiscall*)(void* gameMenuThis);
    using RefreshOptionListFn = void(__thiscall*)(void* thisPtr);
    using ClearRowsFn = void(__thiscall*)(void* thisPtr);

    AddButtonFn g_addButton = nullptr;
    SetNextPageFn g_setNextPage = nullptr;
    SwitchPageFn g_switchPage = nullptr;
    RefreshOptionListFn g_originalRefreshOptionList = nullptr;

    // The live CFCXOptionGamePage instance, captured from the RefreshOptionList detour's own `this`
    // the first time it fires. There is exactly one such object for the session (the engine's boot
    // path constructs it once and keeps it in CGameMenu's hashtable), so caching it is sound -
    // and it is the only way ModPage::RefreshRows can reach the page from a click handler, which
    // has no page pointer of its own.
    void* g_gamePage = nullptr;

    // Set by FCSE's own click handler right before it triggers the switch to the Game page; checked
    // (and cleared) once by the RefreshOptionList hook. This is the entire mechanism that makes the
    // stock "Game" button and FCSE's "Mod Configuration Menu" button - which land on the exact same
    // real page object - show different content.
    bool g_appendPending = false;

    bool g_installed = false; // guards against double-install if this ever runs more than once

    // Every function below wraps exactly one native-pointer touchpoint in SEH and contains no C++
    // objects with destructors (MSVC disallows mixing __try/__except with automatic object
    // unwinding in the same function) - callers do all string/logging work outside these.

    bool SafeReadPointer(void* base, ptrdiff_t offset, void** outValue, DWORD* outCode = nullptr) {
        __try {
            *outValue = *reinterpret_cast<void**>(reinterpret_cast<char*>(base) + offset);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeAddButton(AddButtonFn fn, void* thisPtr, const wchar_t* label, void* handler,
                        DWORD* outCode = nullptr) {
        __try {
            fn(thisPtr, label, 1, handler);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeSetNextPage(SetNextPageFn fn, void* gameMenu, uint32_t* key, DWORD* outCode = nullptr) {
        __try {
            fn(gameMenu, key);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    bool SafeSwitchPage(SwitchPageFn fn, void* gameMenu, DWORD* outCode = nullptr) {
        __try {
            fn(gameMenu);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    // The engine drives this call itself on a normal display, where a fault would be the engine's
    // own to raise. RefreshRows re-enters it from a click handler instead, which is a call site the
    // engine never makes - so it's wrapped here rather than called bare, and both paths share the
    // wrapper so there's only one behaviour to reason about.
    bool SafeRefreshOptionList(void* gamePage, DWORD* outCode = nullptr) {
        __try {
            g_originalRefreshOptionList(gamePage);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    // Calls the real page's own vtable+0x40 slot on itself - the same call RefreshOptionList makes
    // on itself before building native rows (see kClearRowsVtableOffset's comment). Reads the
    // vtable pointer fresh from `gamePage` rather than assuming any fixed vtable address, since it's
    // always the real, natively-constructed page.
    bool SafeClearRows(void* gamePage, DWORD* outCode = nullptr) {
        __try {
            void* vtable = *reinterpret_cast<void**>(gamePage);
            auto fn = *reinterpret_cast<ClearRowsFn*>(reinterpret_cast<char*>(vtable) +
                                                        kClearRowsVtableOffset);
            fn(gamePage);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            if (outCode != nullptr) {
                *outCode = GetExceptionCode();
            }
            return false;
        }
    }

    void LogFailed(const char* what, DWORD code) {
        char buf[160];
        std::snprintf(buf, sizeof(buf), "ModPage: %s raised SEH exception 0x%08lX (caught)", what,
                      static_cast<unsigned long>(code));
        Log::Loader(buf);
    }

    std::wstring WidenAscii(const std::string& s) {
        std::wstring wide;
        wide.reserve(s.size());
        for (char c : s) {
            wide.push_back(static_cast<wchar_t>(static_cast<unsigned char>(c)));
        }
        return wide;
    }

    // Owns every label buffer ever handed to AddButton - never freed, because AddButton's real
    // parameter type is a raw wchar_t* and it has never been confirmed whether the engine copies
    // the text or keeps the pointer. Freeing on the second reading would hand the engine a dangling
    // buffer, so the whole history is kept.
    //
    // That means it grows on every rebuild: once per visit to the page, and now also once per
    // toggle (ModPage::RefreshRows). One rebuild is a header row plus a row per plugin and per
    // setting - a few hundred bytes with a realistic plugin set - so a session of heavy toggling
    // costs kilobytes. Bounded enough to accept; not bounded enough to leave undocumented.
    std::vector<std::wstring>& LabelStorage() {
        static std::vector<std::wstring> storage;
        return storage;
    }

    // Renders one setting's row label. Reads the registry's own live value rather than any cached
    // copy, so the [ON]/[OFF] suffix is correct every time the page is rebuilt.
    std::wstring RowLabel(const SettingsRegistry::Setting& setting) {
        std::wstring label = L"   " + WidenAscii(setting.name);
        switch (setting.value.type) {
        case FCSE_SettingType_Checkbox:
            label += setting.value.asCheckbox ? L" [ON]" : L" [OFF]";
            break;
        }
        return label;
    }

    // Adds a row with no handler behind it - a caption the player can see but not act on.
    bool AppendCaption(AddButtonFn addButton, void* gamePage, std::wstring text, const char* what) {
        DWORD code = 0;
        LabelStorage().push_back(std::move(text));
        if (!SafeAddButton(addButton, gamePage, LabelStorage().back().c_str(),
                            ModsMenuHandler::Create(nullptr), &code)) {
            LogFailed(what, code);
            return false;
        }
        return true;
    }

    // One plugin's block: its name, then a row per setting - or a caption saying it has none, so a
    // plugin without settings still shows up as installed rather than vanishing. `group` is null
    // for exactly that case. Returns false if an AddButton failed, which aborts the whole build.
    bool AppendPluginBlock(AddButtonFn addButton, void* gamePage, const std::string& displayName,
                            const SettingsRegistry::Group* group, size_t* rowCount) {
        if (!AppendCaption(addButton, gamePage, L"-- " + WidenAscii(displayName) + L" --",
                            "AddButton (plugin name row)")) {
            return false;
        }
        ++*rowCount;

        if (group == nullptr || group->settings.empty()) {
            return AppendCaption(addButton, gamePage, L"   (no settings)",
                                  "AddButton (no-settings row)");
        }

        DWORD code = 0;
        for (const std::unique_ptr<SettingsRegistry::Setting>& setting : group->settings) {
            LabelStorage().push_back(RowLabel(*setting));

            ModsMenuHandler* handler = ModsMenuHandler::Create(setting.get());
            if (!SafeAddButton(addButton, gamePage, LabelStorage().back().c_str(), handler, &code)) {
                LogFailed("AddButton (toggle row)", code);
                return false;
            }
            ++*rowCount;
        }
        return true;
    }

    // Appends FCSE's content onto the real CFCXOptionGamePage instance: an unclickable header row
    // (stands in for a real native title change, not pursued this session - see mod_page.h), then
    // one block per loaded plugin (identical mechanism to what mods_tab.cpp already ships on the
    // Options screen itself). Must run *after* CFCXOptionGamePage::RefreshOptionList's own native
    // row-building.
    //
    // The list is driven by what actually loaded, not by what registered settings, so the page
    // answers "which mods do I have?" as well as "what can I change?". A plugin with no settings
    // still gets a row.
    void AppendModContent(AddButtonFn addButton, void* gamePage) {
        if (!AppendCaption(addButton, gamePage, L"=== Mod Configuration Menu ===",
                            "AddButton (header row)")) {
            return;
        }

        const std::vector<std::string>& plugins = PluginLoader::LoadedNames();
        if (plugins.empty()) {
            AppendCaption(addButton, gamePage, L"   (no plugins installed)",
                           "AddButton (empty-list row)");
            Log::Loader("ModPage: no plugins loaded, nothing to list");
            return;
        }

        size_t rowCount = 0;
        for (const std::string& plugin : plugins) {
            if (!AppendPluginBlock(addButton, gamePage, plugin, SettingsRegistry::FindGroup(plugin),
                                    &rowCount)) {
                return;
            }
        }

        // A plugin picks its own registration name and is free to make it something other than its
        // module name ("example_plugin" vs "Example Mod"). Those groups match no entry in the list
        // above, so they get their own blocks here - showing them under a name the player won't
        // recognise beats silently hiding settings that exist and are in fcse.ini.
        for (const SettingsRegistry::Group& group : SettingsRegistry::Groups()) {
            bool named = false;
            for (const std::string& plugin : plugins) {
                if (plugin == group.pluginName) {
                    named = true;
                    break;
                }
            }
            if (named) {
                continue;
            }
            if (!AppendPluginBlock(addButton, gamePage, group.pluginName, &group, &rowCount)) {
                return;
            }
        }

        Log::Loader("ModPage: appended " + std::to_string(rowCount) +
                     " row(s) to the Game tab (captions not counted)");
    }

    // Hand-rolled IMenuItemHandler for the Options row's click - same shape/technique as
    // ModsMenuHandler (menu_handler.h): a plain struct whose first member is a vtable pointer, one
    // real slot (kActivateSlot, empirically confirmed correct - see menu_handler.h), the rest safe
    // no-ops. Reaches the real Game page directly via SetNextPage+SwitchPage (the same two native
    // calls CSetNextPageMenuHandler::SwitchPage itself makes) rather than owning a real
    // CSetNextPageMenuHandler instance - simpler, and this is the one place that needs to also set
    // g_appendPending right before the switch.
    struct McmNavigationHandler {
        void** vtable; // must stay first - what the engine's click-dispatch code reads
        void* ownerPage; // the Options page - ownerPage+0x140 gives the live CGameMenu* at click time

        unsigned int SafeNoOp(unsigned int /*arg*/) { return 0; }

        unsigned int OnActivate(unsigned int /*arg*/) {
            DWORD code = 0;
            void* gameMenu = nullptr;
            if (!SafeReadPointer(ownerPage, kOwnerPageToGameMenuOffset, &gameMenu, &code)) {
                LogFailed("reading ownerPage+0x140 at click time", code);
                return 0;
            }
            if (gameMenu == nullptr || g_setNextPage == nullptr || g_switchPage == nullptr) {
                Log::Loader("ModPage: no CGameMenu* or navigation functions unavailable, click "
                            "ignored");
                return 0;
            }

            g_appendPending = true;

            static uint32_t s_pageId = kGameOptionsPageId;
            if (!SafeSetNextPage(g_setNextPage, gameMenu, &s_pageId, &code)) {
                LogFailed("CGameMenu::SetNextPage", code);
                g_appendPending = false;
                return 0;
            }
            if (!SafeSwitchPage(g_switchPage, gameMenu, &code)) {
                LogFailed("CGameMenu::SwitchPage", code);
                g_appendPending = false;
                return 0;
            }
            Log::Loader("ModPage: switched to the Game tab via Mod Configuration Menu");
            return 0;
        }

        static McmNavigationHandler* Create(void* ownerPage);
    };

    constexpr int kVtableSlotCount = 8;
    constexpr int kActivateSlot = 1; // matches menu_handler.cpp's empirically-confirmed slot

    using McmHandlerMemberFn = unsigned int (McmNavigationHandler::*)(unsigned int);

    void* RawFunctionPointer(McmHandlerMemberFn fn) {
        union {
            McmHandlerMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_mcmHandlerVtable[kVtableSlotCount];
    bool g_mcmHandlerVtableReady = false;

    void** EnsureMcmHandlerVtable() {
        if (!g_mcmHandlerVtableReady) {
            void* noOp = RawFunctionPointer(&McmNavigationHandler::SafeNoOp);
            for (void*& slot : g_mcmHandlerVtable) {
                slot = noOp;
            }
            g_mcmHandlerVtable[kActivateSlot] = RawFunctionPointer(&McmNavigationHandler::OnActivate);
            g_mcmHandlerVtableReady = true;
        }
        return g_mcmHandlerVtable;
    }

    McmNavigationHandler* McmNavigationHandler::Create(void* ownerPage) {
        auto* handler = new McmNavigationHandler();
        handler->vtable = EnsureMcmHandlerVtable();
        handler->ownerPage = ownerPage;
        return handler;
    }

    // MSVC won't let a free function be declared __thiscall (only real member functions), but the
    // hook target genuinely is __thiscall-with-no-stack-args (this in ECX only) - same pattern
    // mods_tab.cpp already uses for CFCXOptionPage::Setup.
    struct RefreshDetourThunk {
        void Detour();
    };

    // The whole row-building sequence, shared by the engine's own display path (the detour below)
    // and FCSE's post-toggle refresh (ModPage::RefreshRows). Consumes g_appendPending.
    void RebuildRows(void* gamePage) {
        // Consumed up front, not after the native build: this transition's intent is spent either
        // way, and leaving the flag set through a failure would make the *next* display show FCSE's
        // rows - including one reached by the stock "Game" button, which never sets it.
        bool withModContent = g_appendPending;
        g_appendPending = false;

        DWORD code = 0;
        if (!SafeRefreshOptionList(gamePage, &code)) { // native build runs fully either way
            LogFailed("CFCXOptionGamePage::RefreshOptionList", code);
            return; // the row list is in an unknown state - appending onto it would compound that
        }

        if (!withModContent) {
            return;
        }

        if (!SafeClearRows(gamePage, &code)) {
            LogFailed("clearing native rows before mod content", code);
            // Fall through and append anyway - worst case FCSE's rows sit below native ones,
            // rather than losing FCSE's content entirely.
        }
        AppendModContent(g_addButton, gamePage);
    }

    void RefreshDetourThunk::Detour() {
        void* gamePage = reinterpret_cast<void*>(this);
        g_gamePage = gamePage; // the only place a real page pointer is ever handed to us
        RebuildRows(gamePage);
    }

    using RefreshDetourMemberFn = void (RefreshDetourThunk::*)();

    void* RawFunctionPointer(RefreshDetourMemberFn fn) {
        union {
            RefreshDetourMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }
} // namespace

void ModPage::Install(void* optionsMenuThis) {
    if (g_installed) {
        return;
    }
    g_installed = true;

    uintptr_t base = DuniaApi::Base();
    if (base == 0 || optionsMenuThis == nullptr) {
        Log::Loader("ModPage: Dunia.dll not resolved or no owner page, skipping");
        return;
    }

    g_addButton = reinterpret_cast<AddButtonFn>(base + (kAddButtonRva - kDuniaPreferredBase));
    g_setNextPage = reinterpret_cast<SetNextPageFn>(base + (kSetNextPageRva - kDuniaPreferredBase));
    g_switchPage = reinterpret_cast<SwitchPageFn>(base + (kSwitchPageRva - kDuniaPreferredBase));

    // Hook RefreshOptionList so FCSE's content gets appended every time the Game tab is displayed
    // via FCSE's own button - not once, early - and never when reached via the stock "Game" button.
    void* refreshTarget =
        reinterpret_cast<void*>(base + (kRefreshOptionListRva - kDuniaPreferredBase));
    if (!HookManager::Hook(refreshTarget, RawFunctionPointer(&RefreshDetourThunk::Detour),
                            reinterpret_cast<void**>(&g_originalRefreshOptionList))) {
        Log::Loader("ModPage: failed to install RefreshOptionList hook - no mod content on the "
                    "Game tab this session");
        return;
    }
    Log::Loader("ModPage: RefreshOptionList hook installed");

    McmNavigationHandler* handler = McmNavigationHandler::Create(optionsMenuThis);
    DWORD code = 0;
    if (!SafeAddButton(g_addButton, optionsMenuThis, L"Mod Configuration Menu", handler, &code)) {
        LogFailed("AddButton (Options row)", code);
        Log::Loader("ModPage: Options row was not added this session");
        return;
    }
    Log::Loader("ModPage: added \"Mod Configuration Menu\" row to Options");
}

void ModPage::RefreshRows() {
    if (g_gamePage == nullptr || g_originalRefreshOptionList == nullptr) {
        Log::Loader("ModPage: refresh requested before the Game page was ever displayed, ignored");
        return;
    }

    // This re-enters the engine's row list from inside its own click dispatch: the row that was
    // just clicked is destroyed by the clear below, while the engine may still be holding it. The
    // handler object itself survives (menu_handler.h's handlers are heap-allocated and never
    // freed), which removes the most obvious hazard, but the engine's own post-Activate bookkeeping
    // is not something FCSE controls. Every native touchpoint inside RebuildRows is SEH-wrapped, so
    // the failure mode is a logged, caught exception and a stale label rather than a hard crash.
    //
    // If this ever does prove unsafe in practice, the fallback needing no engine re-entry at all is
    // to overwrite the label buffer in place: LabelStorage() owns those strings and AddButton was
    // handed a raw wchar_t* into them. That needs the [ON]/[OFF] suffixes padded to equal length so
    // the buffer never reallocates, and only works if the engine stored the pointer rather than
    // copying the text - which is exactly the thing that has never been confirmed either way.
    g_appendPending = true;
    RebuildRows(g_gamePage);
}

} // namespace FCSE
