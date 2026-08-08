#include "fcse_page.h"

#include "dunia_api.h"
#include "ini_file.h"
#include "log.h"
#include "hook.h"
#include "magma_package.h"
#include "plugin_loader.h"
#include "settings_registry.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <string>
#include <vector>
#include <windows.h>

// Dunia.dll (Steam v1.03) RVAs, same base-plus-RVA convention mods_tab.cpp uses. Every address and
// offset below was confirmed by decompile and then exercised in-game during the 2026-08-07 spike;
// the trail is in PLAN-own-page.md (git history, removed in cf13c2b).
namespace FCSE {

namespace {
    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;

    // CFCXOptionGamePage's real ctor, and the allocation size AddPage<CFCXOptionGamePage> uses.
    //
    // Deliberately the *concrete leaf*, not the tidier-looking CFCXBaseOptionPage one class below
    // it (0x1087ec80). That base was tried on 2026-08-08 and is **abstract**: constructing it works
    // and the page displays, but Back kills the process with R6025 "pure virtual function call".
    // Its content-build slot at vtable+0x3c being a plain RET says nothing about the rest of the
    // table; do not read that as "concrete" again.
    constexpr uintptr_t kGamePageCtorRva = 0x1081e9c0;
    constexpr size_t kPageSize = 0x210;

    // CUIPageBase::Init - turns the page's authored name into a bound magma::Page and its widgets.
    // Nothing in the engine calls it implicitly, which is why every earlier hand-built page
    // displayed nothing and then faulted.
    constexpr uintptr_t kUiPageBaseInitRva = 0x10109410;

    constexpr uintptr_t kAddButtonRva = 0x10cdbb80;
    constexpr uintptr_t kSwitchPageRva = 0x101d1990;

    // CFCXOptionGamePage::RefreshOptionList - the per-page content builder, which reruns every time
    // the page is displayed rather than once at construction. FCSE's rows have to be appended from
    // inside it: anything added at construction time is wiped by the clear it opens with.
    //
    // The hook is global because it is a class method and FCSE's page shares the class, so the
    // detour has to tell the two instances apart by `this`.
    constexpr uintptr_t kRefreshOptionListRva = 0x10820160;

    // The virtual slot RefreshOptionList itself calls first, before building any native rows -
    // the engine's own "clear my row list". Reused to drop the native settings from FCSE's page.
    constexpr ptrdiff_t kClearRowsVtableOffset = 0x40;

    // CSettingsPage::AddBoolSetting - the engine's own "label plus a YES/NO control" row, which is
    // what makes FCSE's rows look like the stock ones instead of a caption with an [ON]/[OFF]
    // suffix glued on. Read off CFCXOptionGamePage::RefreshOptionList, which calls it once per
    // boolean setting on the Game tab.
    //
    //   AddBoolSetting(this, label, labelListParam, settingParam, yesText, noText, enabled, handler)
    //
    // Its body forwards `label` and `handler` straight to CListMenuPage::AddButton, so the label is
    // a plain wchar_t* - the localised strings the stock page passes are incidental, not a required
    // type. It then binds the value widget with
    // CUISettingBase::FetchMagmaElements(page->m_magmaPage, labelListParam, settingParam), which is
    // the lookup that resolves "FCSE_SLOT_nn" against this page's own UserData.
    constexpr uintptr_t kAddBoolSettingRva = 0x10cde0d0;

    // The localised "YES"/"NO" strings RefreshOptionList lazily builds and caches. Reusing them
    // means FCSE's toggles read in the player's own language, with no localisation work here.
    // RefreshOptionList runs before AppendRows on every display (it is the function this content is
    // appended from the tail of), so both are populated by the time they are read.
    constexpr uintptr_t kYesTextGlobalRva = 0x1164fca4;
    constexpr uintptr_t kNoTextGlobalRva = 0x1164fca0;

    // The UserData property naming this page's row list, and the per-row value-widget properties.
    // Both must match what fcse.mgb declares - see tools/FCSE/assets/README.md.
    constexpr char kLabelListParam[] = "SETTING_LABEL_LIST";
    constexpr size_t kSlotCount = 20;

    // CValueListSetting's own value accessors, on the object AddBoolSetting returns (vtable
    // PTR_FUN_10eb1f38). Both take/return a *pointer* to the value: SetValue (0x10864250) scans the
    // value array AddBoolSetting filled with {true, false} and selects the matching row in the
    // bound YES/NO list. That the getter also returns a pointer is not a guess - the object's own
    // slot 9 (0x10cde2f0) is literally `SetValue(GetValue())`.
    constexpr size_t kSettingSetValueSlot = 13; // vtable +0x34
    constexpr size_t kSettingGetValueSlot = 14; // vtable +0x38

    // magma::TextBase::SetText - takes a RAW wchar_t*, so no string object has to be forged.
    constexpr uintptr_t kTextBaseSetTextRva = 0x1007d770;

    // CUIPageBase's page-name std::string. The object starts at +0x28 with its embedded allocator;
    // these three are the fields Init itself reads, so overwriting only them leaves the ctor's
    // allocator in place. Init branches on `capacity < 0x10` to decide whether the characters live
    // inline at +0x2c or behind a pointer there - which is why the name is kept short.
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

    // Must match the area name authored into fcse.mgb and registered in its GenericObjectTable.
    // 9 characters, so it lives inline in the page's own SSO buffer - no allocation, no heap
    // ownership, no CryStringBase refcount emulation. See tools/FCSE/assets/README.md.
    constexpr char kPageName[] = "FCSE_PAGE";
    constexpr wchar_t kTitle[] = L"Mod Configuration";

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
    using RefreshOptionListFn = void(__thiscall*)(void* thisPtr);
    using ClearRowsFn = void(__thiscall*)(void* thisPtr);
    using AddBoolSettingFn = void*(__thiscall*)(void* page, const wchar_t* label,
                                                const char* labelListParam, const char* settingParam,
                                                void* yesText, void* noText, int enabled,
                                                void* handler);

    GamePageCtorFn g_gamePageCtor = nullptr;
    InitFn g_init = nullptr;
    AddButtonFn g_addButton = nullptr;
    SwitchPageFn g_switchPage = nullptr;
    SetTextFn g_textBaseSetText = nullptr;
    RefreshOptionListFn g_originalRefreshOptionList = nullptr;
    AddBoolSettingFn g_addBoolSetting = nullptr;
    void** g_yesText = nullptr;
    void** g_noText = nullptr;

    void* g_page = nullptr;
    bool g_installed = false;
    bool g_nativeToggles = false;

    // Each SEH wrapper holds exactly one native touchpoint and no C++ object with a destructor -
    // MSVC forbids mixing __try/__except with automatic unwinding in one function.

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

    bool SafeRefreshOptionList(void* page, DWORD* outCode) {
        __try {
            g_originalRefreshOptionList(page);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeClearRows(void* page, DWORD* outCode) {
        __try {
            void* vtable = *reinterpret_cast<void**>(page);
            auto fn = *reinterpret_cast<ClearRowsFn*>(reinterpret_cast<char*>(vtable) +
                                                      kClearRowsVtableOffset);
            fn(page);
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
        std::snprintf(buf, sizeof(buf), "FcsePage: %s raised SEH exception 0x%08lX (caught)", what,
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
        std::snprintf(buf, sizeof(buf), "FcsePage:   %s (+0x%02X) = 0x%08X", what,
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
    // binds the title widget at +0x10. Note CMenuPage::Display does NOT re-apply the stored title on
    // this build (unlike the ELF), which is exactly why the widget push is not optional.
    void SetPageTitle(void* page, const wchar_t* title) {
        OverwriteString<wchar_t, kWideSsoCapacity>(page, kTitleDataOffset, kTitleSizeOffset,
                                                    kTitleCapacityOffset, title, std::wcslen(title));
        DWORD code = 0;
        void* titleText = nullptr;
        if (!SafeReadPointer(page, kTitleTextOffset, &titleText, &code) || titleText == nullptr) {
            Log::Loader("FcsePage: no title TextBase bound, stored the string only");
            return;
        }
        if (!SafeSetText(titleText, title, &code)) {
            LogFailed("magma::TextBase::SetText", code);
        }
    }

    // Hand-rolled IMenuItemHandler: a struct whose first member is a vtable pointer, one real slot,
    // the rest safe no-ops. kActivateSlot = 1 is not a guess - an instrumented run on 2026-08-08
    // logged slot 1 and only slot 1 for a row click, and slot 0 is independently known to be the
    // MSVC scalar deleting destructor.
    struct NavigationHandler {
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
                Log::Loader("FcsePage: no CGameMenu* or page unavailable, click ignored");
                return 0;
            }
            if (!SafeWritePointer(gameMenu, kGameMenuNextPageOffset, targetPage, &code)) {
                LogFailed("writing CGameMenu+0x3c", code);
                return 0;
            }
            if (!SafeCall(reinterpret_cast<void(__thiscall*)(void*)>(g_switchPage), gameMenu,
                          &code)) {
                LogFailed("CGameMenu::SwitchPage", code);
            }
            return 0;
        }

        static NavigationHandler* Create(void* ownerPage, void* targetPage);
    };

    constexpr int kVtableSlotCount = 8;
    constexpr int kActivateSlot = 1;

    using HandlerMemberFn = unsigned int (NavigationHandler::*)(unsigned int);

    void* RawFunctionPointer(HandlerMemberFn fn) {
        union {
            HandlerMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_handlerVtable[kVtableSlotCount];
    bool g_handlerVtableReady = false;

    NavigationHandler* NavigationHandler::Create(void* ownerPage, void* targetPage) {
        if (!g_handlerVtableReady) {
            void* noOp = RawFunctionPointer(&NavigationHandler::SafeNoOp);
            for (void*& slot : g_handlerVtable) {
                slot = noOp;
            }
            g_handlerVtable[kActivateSlot] = RawFunctionPointer(&NavigationHandler::OnActivate);
            g_handlerVtableReady = true;
        }
        auto* handler = new NavigationHandler();
        handler->vtable = g_handlerVtable;
        handler->ownerPage = ownerPage;
        handler->targetPage = targetPage;
        return handler;
    }

    // Isolated so the __try can exist at all: MSVC forbids mixing SEH with C++ objects that have
    // destructors, and ToggleCheckbox itself is full of them.
    bool SafeToggle(SettingsRegistry::Setting* setting, DWORD* outCode) {
        __try {
            SettingsRegistry::ToggleCheckbox(setting);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // Returns the CValueListSetting the engine built for this row, so its value can be seeded now
    // and read back later. Handler is always null - see the comment at the call site.
    bool SafeAddBoolSetting(void* page, const wchar_t* label, const char* slotParam,
                            void** outSetting, DWORD* outCode) {
        __try {
            *outSetting = g_addBoolSetting(page, label, kLabelListParam, slotParam, *g_yesText,
                                           *g_noText, 1, nullptr);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // The fields AddBoolSetting initialises on the CValueListSetting it creates:
    //   +0x44 the bound value widget, +0x4c the value array, +0x50 its length in bytes.
    struct SettingFields {
        unsigned widget;
        unsigned values;
        unsigned valuesLength;
    };

    bool SafeReadSettingFields(void* settingObject, SettingFields* out, DWORD* outCode) {
        __try {
            auto base = reinterpret_cast<unsigned char*>(settingObject);
            out->widget = *reinterpret_cast<unsigned*>(base + 0x44);
            out->values = *reinterpret_cast<unsigned*>(base + 0x4c);
            out->valuesLength = *reinterpret_cast<unsigned*>(base + 0x50);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    using SettingSetValueFn = void(__thiscall*)(void* self, const bool* value);
    using SettingGetValueFn = const bool*(__thiscall*)(void* self);

    bool SafeSetSettingValue(void* settingObject, bool value, DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(settingObject);
            auto setValue = reinterpret_cast<SettingSetValueFn>(vtable[kSettingSetValueSlot]);
            setValue(settingObject, &value);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeGetSettingValue(void* settingObject, bool* outValue, DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(settingObject);
            auto getValue = reinterpret_cast<SettingGetValueFn>(vtable[kSettingGetValueSlot]);
            const bool* value = getValue(settingObject);
            if (value == nullptr) {
                return false;
            }
            *outValue = *value;
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // Owns every label ever handed to the engine. Never freed, because AddButton is only known to
    // store the pointer rather than copy the text.
    //
    // A deque rather than a vector: appending to a vector reallocates and moves the wstring
    // objects, which is fine for a long label (the characters stay put on the heap) but would
    // dangle for any short enough to live in the string's SSO buffer. A deque keeps references to
    // existing elements valid, so a label's address is stable for the session - which is what makes
    // the in-place rewrite below safe.
    std::deque<std::wstring>& LabelStorage() {
        static std::deque<std::wstring> storage;
        return storage;
    }

    // Kept the same width so the rows line up in a proportional-ish menu font.
    constexpr wchar_t kOnSuffix[] = L"[ON] ";
    constexpr wchar_t kOffSuffix[] = L"[OFF]";

    std::wstring WidenAscii(const std::string& text) {
        return std::wstring(text.begin(), text.end());
    }

    void RebuildRows(void* page); // defined below, next to the RefreshOptionList detour

    // The click handler for toggle rows: flip the value, then rebuild the page so the row's
    // [ON]/[OFF] reflects it immediately.
    //
    // The rebuild re-enters the engine's row list from inside its own click dispatch, which is a
    // known hazard - the row being dispatched is destroyed while the engine may still hold it. It
    // is used anyway because it is the only thing that actually refreshes the label, and because
    // the evidence exonerates it: the crash this page hit earlier came from native AddBoolSetting
    // rows, where FCSE had no code in the click path at all, and toggling with a rebuild is the
    // mechanism the shipped Mod Configuration Menu has always used. Two alternatives were tried and
    // rejected - deferring the refresh to the next display (correct, but the label visibly lags a
    // click behind) and rewriting the label buffer in place (no engine re-entry at all, but nothing
    // redraws, so the engine copies the text at AddButton time rather than storing the pointer).
    struct ToggleHandler {
        void** vtable; // must stay first
        SettingsRegistry::Setting* setting;

        unsigned int SafeNoOp(unsigned int /*arg*/) { return 0; }

        unsigned int OnActivate(unsigned int /*arg*/) {
            if (setting == nullptr) {
                return 0; // caption rows exist to be read, not clicked
            }
            DWORD code = 0;
            if (!SafeToggle(setting, &code)) {
                LogFailed("SettingsRegistry::ToggleCheckbox", code);
                return 0;
            }
            RebuildRows(g_page);
            return 0;
        }

        static ToggleHandler* Create(SettingsRegistry::Setting* setting);
    };

    using ToggleHandlerMemberFn = unsigned int (ToggleHandler::*)(unsigned int);

    void* RawToggleHandlerPointer(ToggleHandlerMemberFn fn) {
        union {
            ToggleHandlerMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_toggleVtable[kVtableSlotCount];
    bool g_toggleVtableReady = false;

    ToggleHandler* ToggleHandler::Create(SettingsRegistry::Setting* setting) {
        if (!g_toggleVtableReady) {
            void* noOp = RawToggleHandlerPointer(&ToggleHandler::SafeNoOp);
            for (void*& slot : g_toggleVtable) {
                slot = noOp;
            }
            g_toggleVtable[kActivateSlot] = RawToggleHandlerPointer(&ToggleHandler::OnActivate);
            g_toggleVtableReady = true;
        }
        auto* handler = new ToggleHandler();
        handler->vtable = g_toggleVtable;
        handler->setting = setting;
        return handler;
    }

    // NOTE: the native AddBoolSetting path below has no handler of its own. An earlier version
    // one here and passed it to AddBoolSetting; clicking a row then crashed the game to desktop,
    // before any FCSE code ran - the instrumented log showed the handler was never entered. Every
    // boolean row on the stock Game tab passes handler = 0, because a settings row is driven by
    // its CValueListSetting and by widget events rather than by a button handler. Values are read
    // back from the control on the next rebuild instead; see SyncValuesFromControls.

    // The rows built by the previous display, so their controls can be read back before the list is
    // cleared. Rebuilt from scratch every display.
    struct LiveRow {
        SettingsRegistry::Setting* setting;
        void* settingObject;
    };

    std::vector<LiveRow>& LiveRows() {
        static std::vector<LiveRow> rows;
        return rows;
    }

    // Persistence, without a click handler. The engine owns the YES/NO control and changes it in
    // place; FCSE notices on the next rebuild, which happens on every display and therefore also
    // when the player backs out and returns.
    //
    // The cost is that fcse.ini is written when the page is next shown rather than on the click
    // itself. The benefit is that nothing of FCSE's runs inside the engine's click dispatch, which
    // is what was crashing the game.
    void SyncValuesFromControls() {
        for (const LiveRow& row : LiveRows()) {
            bool shown = false;
            DWORD code = 0;
            if (!SafeGetSettingValue(row.settingObject, &shown, &code)) {
                if (code != 0) {
                    LogFailed("CValueListSetting::GetValue", code);
                }
                continue;
            }
            if (shown == (row.setting->value.asCheckbox != 0)) {
                continue;
            }
            Log::Loader("FcsePage: \"" + row.setting->name + "\" changed to " +
                        (shown ? "ON" : "OFF") + " - persisting");
            if (!SafeToggle(row.setting, &code)) {
                LogFailed("SettingsRegistry::ToggleCheckbox", code);
            }
        }
        LiveRows().clear();
    }

    void AppendCaption(void* page, const std::wstring& text, size_t* row) {
        if (*row >= kSlotCount) {
            return;
        }
        LabelStorage().push_back(text);
        DWORD code = 0;
        if (!SafeAddButton(page, LabelStorage().back().c_str(), nullptr, &code)) {
            LogFailed("AddButton (caption row)", code);
            return;
        }
        ++*row;
    }

    void AppendPluginBlock(void* page, const std::string& displayName,
                           const SettingsRegistry::Group* group, size_t* row) {
        AppendCaption(page, L"-- " + WidenAscii(displayName) + L" --", row);

        if (group == nullptr || group->settings.empty()) {
            AppendCaption(page, L"   (no settings)", row);
            return;
        }

        for (const std::unique_ptr<SettingsRegistry::Setting>& setting : group->settings) {
            if (setting->value.type != FCSE_SettingType_Checkbox) {
                // Sliders and choice lists need AddSliderSetting / AddValueListSetting and their own
                // slot templates (common.mgb 62EA6603 rather than 652FD37C); until those exist,
                // saying so beats rendering a control that cannot work.
                AppendCaption(page, L"   " + WidenAscii(setting->name) + L" (unsupported type)", row);
                continue;
            }
            if (*row >= kSlotCount) {
                // The layout declares exactly kSlotCount value widgets. Past that the lookup would
                // miss and the row would appear with no control at all, which is worse than an
                // honest message.
                Log::Loader("FcsePage: out of value slots (" + std::to_string(kSlotCount) +
                            "), skipping \"" + setting->name + "\" and anything after it");
                return;
            }

            if (!g_nativeToggles) {
                // The shipping path: a plain row whose label carries the value. Not as pretty as a
                // native YES/NO control, but it works, and it is the same mechanism the Mod
                // Configuration Menu has always used.
                LabelStorage().push_back(L"   " + WidenAscii(setting->name) + L"   " +
                                         (setting->value.asCheckbox ? kOnSuffix : kOffSuffix));
                DWORD captionCode = 0;
                if (!SafeAddButton(page, LabelStorage().back().c_str(),
                                   ToggleHandler::Create(setting.get()), &captionCode)) {
                    LogFailed("AddButton (toggle row)", captionCode);
                    return;
                }
                ++*row;
                continue;
            }

            char slotParam[24];
            sprintf_s(slotParam, "FCSE_SLOT_%02zu", *row + 1);

            LabelStorage().push_back(L"   " + WidenAscii(setting->name));

            // No click handler, deliberately. Every boolean row on the stock Game tab passes 0
            // here, because a settings row is driven by the CValueListSetting attached to it and
            // by widget events - not by a button handler. Passing FCSE's hand-rolled handler put
            // the engine down a path that fake vtable cannot satisfy and crashed the game on the
            // click, before any FCSE code ran. Changes are picked up by reading the control back
            // instead, in SyncValuesFromControls above.
            void* settingObject = nullptr;
            DWORD code = 0;
            if (!SafeAddBoolSetting(page, LabelStorage().back().c_str(), slotParam, &settingObject,
                                    &code)) {
                LogFailed("CSettingsPage::AddBoolSetting", code);
                return;
            }
            ++*row;

            if (settingObject == nullptr) {
                continue;
            }

            // Whether FCSE_SLOT_nn actually resolved. AddBoolSetting binds the value widget into
            // +0x44 and guards *both* of its YES/NO item-adds on that field being non-null - so an
            // unresolved slot leaves the value array empty, and everything that later walks it (the
            // engine on click, SetValue here) dereferences nothing. Logged rather than assumed,
            // because a row with no control looks identical to a working one until it is used.
            SettingFields fields{};
            if (!SafeReadSettingFields(settingObject, &fields, &code)) {
                LogFailed("reading the CValueListSetting's fields", code);
                continue;
            }
            char detail[192];
            sprintf_s(detail, "FcsePage: %s -> setting=0x%08X widget=0x%08X values=0x%08X len=%u",
                      slotParam, reinterpret_cast<unsigned>(settingObject), fields.widget,
                      fields.values, fields.valuesLength);
            Log::Loader(detail);

            if (fields.widget == 0 || fields.values == 0 || fields.valuesLength == 0) {
                Log::Loader(std::string("FcsePage: ") + slotParam +
                            " did not bind a value widget - the row has no control, so it is left "
                            "unseeded and unread");
                continue;
            }

            // Seed the control from the registry, or the row would show whatever the list happens
            // to start on rather than the value in fcse.ini.
            if (!SafeSetSettingValue(settingObject, setting->value.asCheckbox != 0, &code)) {
                LogFailed("CValueListSetting::SetValue", code);
                continue;
            }
            LiveRows().push_back({setting.get(), settingObject});
        }
    }

    bool ReadFlag(const char* key) {
        IniFile ini;
        if (!ini.Load(Log::LoaderDirectory() + L"fcse.ini")) {
            return false;
        }
        const std::string* value = ini.Find("FCSE", key);
        return value != nullptr && (*value == "true" || *value == "1");
    }

    // The whole row-building sequence, shared by the engine's own display path (the detour) and by
    // a row's click handler. Runs the native build first - it is the thing that binds the page's
    // widgets and initialises the localised YES/NO strings - then drops its rows and appends FCSE's.
    void RebuildRows(void* page) {
        DWORD code = 0;
        if (!SafeRefreshOptionList(page, &code)) {
            LogFailed("CFCXOptionGamePage::RefreshOptionList", code);
            return; // the row list is in an unknown state; appending would compound that
        }
        if (!SafeClearRows(page, &code)) {
            LogFailed("clearing the native rows", code);
            // Fall through: FCSE's rows below the native ones beats losing them entirely.
        }
        FcsePage::AppendRows(page);
    }

    // MSVC will not let a free function be __thiscall, so the detour is a real member function on a
    // throwaway type - `this` is the engine's page, never an instance of this struct.
    struct RefreshDetourThunk {
        void Detour();
    };

    void RefreshDetourThunk::Detour() {
        void* page = reinterpret_cast<void*>(this);
        if (!FcsePage::OwnsPage(page)) {
            // The stock Game tab, reached by its own button. Left completely alone - the whole
            // point of having our own page is that FCSE no longer touches it.
            g_originalRefreshOptionList(page);
            return;
        }
        RebuildRows(page);
    }

    using RefreshDetourMemberFn = void (RefreshDetourThunk::*)();

    void* RawDetourPointer(RefreshDetourMemberFn fn) {
        union {
            RefreshDetourMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    // Opt in to the engine's own YES/NO controls via CSettingsPage::AddBoolSetting instead of rows
    // whose label carries the value.
    //
    // OFF by default because clicking such a row crashes the game, and the cause is not FCSE's.
    // Measured 2026-08-08: the row is built correctly - FCSE_SLOT_nn resolves, the value widget
    // binds, both YES/NO entries are added, and SetValue succeeds - and the crash still happens on
    // the click, inside the engine's own handling of the CValueListSetting it attaches to the row.
    // AddBoolSetting overwrites whatever handler it is given with that setting object, so FCSE has
    // no code in that path at all.
    //
    // Everything needed to pick this back up is in PLAN-own-page.md (git history, removed in
    // cf13c2b); it is one unknown away.
    bool NativeToggles() { return ReadFlag("Own page native toggles"); }

}

bool FcsePage::Install(void* optionsMenuThis) {
    if (g_installed) {
        return g_page != nullptr;
    }
    g_installed = true;
    g_nativeToggles = NativeToggles();
    if (g_nativeToggles) {
        Log::Loader("FcsePage: \"Own page native toggles\" is set - using AddBoolSetting. Clicking a "
                    "row is known to crash the game; this is a diagnostic mode.");
    }

    uintptr_t base = DuniaApi::Base();
    if (base == 0 || optionsMenuThis == nullptr) {
        Log::Loader("FcsePage: Dunia.dll not resolved or no owner page, skipping");
        return false;
    }
    if (!MagmaPackage::Loaded()) {
        Log::Loader("FcsePage: fcse.mgb is not loaded, so \"FCSE_PAGE\" cannot resolve - not "
                    "offering a page that would display nothing");
        return false;
    }

    auto resolve = [base](uintptr_t rva) { return base + (rva - kDuniaPreferredBase); };
    g_gamePageCtor = reinterpret_cast<GamePageCtorFn>(resolve(kGamePageCtorRva));
    g_init = reinterpret_cast<InitFn>(resolve(kUiPageBaseInitRva));
    g_addButton = reinterpret_cast<AddButtonFn>(resolve(kAddButtonRva));
    g_switchPage = reinterpret_cast<SwitchPageFn>(resolve(kSwitchPageRva));
    g_textBaseSetText = reinterpret_cast<SetTextFn>(resolve(kTextBaseSetTextRva));
    g_addBoolSetting = reinterpret_cast<AddBoolSettingFn>(resolve(kAddBoolSettingRva));
    g_yesText = reinterpret_cast<void**>(resolve(kYesTextGlobalRva));
    g_noText = reinterpret_cast<void**>(resolve(kNoTextGlobalRva));

    // Hooked before the page is constructed: Init triggers a display, and the rows have to be
    // appended from inside the rebuild rather than added directly.
    void* refreshTarget = reinterpret_cast<void*>(resolve(kRefreshOptionListRva));
    if (!HookManager::Hook(refreshTarget, RawDetourPointer(&RefreshDetourThunk::Detour),
                           reinterpret_cast<void**>(&g_originalRefreshOptionList))) {
        Log::Loader("FcsePage: failed to hook RefreshOptionList - the page would display with no "
                    "rows, so it is not offered this session");
        return false;
    }

    // Zero-initialized: CListMenuPage's own base-class fields (the row array and friends) are never
    // written by the ctor, and zero is what "empty row list" means.
    void* page = new unsigned char[kPageSize]();
    DWORD code = 0;
    if (!SafeCall(reinterpret_cast<void(__thiscall*)(void*)>(g_gamePageCtor), page, &code)) {
        LogFailed("CFCXOptionGamePage::CFCXOptionGamePage", code);
        return false;
    }

    // Retarget the page at our own layout by overwriting the name the ctor stored. The ctor's own
    // allocator field at +0x28 is left untouched.
    OverwriteString<char, kNarrowSsoCapacity>(page, kPageNameDataOffset, kPageNameSizeOffset,
                                               kPageNameCapacityOffset, kPageName,
                                               std::strlen(kPageName));

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
    Log::Loader("FcsePage: CUIPageBase::Init returned - bound state follows");
    LogPointer("magma::Page", page, kBoundMagmaPageOffset);
    LogPointer("row list Element", page, kRowListElementOffset);
    LogPointer("row ListBox", page, kRowListBoxOffset);
    LogPointer("title TextBase", page, kTitleTextOffset);
    LogPointer("inited flag", page, kInitedFlagOffset);

    void* boundPage = nullptr;
    if (SafeReadPointer(page, kBoundMagmaPageOffset, &boundPage, &code) && boundPage == nullptr) {
        Log::Loader("FcsePage: no magma::Page bound - \"FCSE_PAGE\" did not resolve through any "
                    "loaded package's GenericObjectTable, even though fcse.mgb reported loaded. "
                    "Not adding the Options row.");
        return false;
    }

    g_page = page;
    SetPageTitle(page, kTitle);

    // Rows are NOT added here - RefreshOptionList clears the row list on every display, so anything
    // appended now is wiped first. AppendRows runs from inside that rebuild instead.

    NavigationHandler* handler = NavigationHandler::Create(optionsMenuThis, page);
    if (!SafeAddButton(optionsMenuThis, L"Mod Configuration Menu", handler, &code)) {
        LogFailed("AddButton (Options row)", code);
        return false;
    }
    Log::Loader("FcsePage: installed - added the Options row for the private page");
    return true;
}

bool FcsePage::OwnsPage(void* page) {
    return g_page != nullptr && page == g_page;
}

void FcsePage::AppendRows(void* page) {
    // Re-applied per display: the native rebuild this runs inside may have reset the title along
    // with the rows, and it is cheap enough not to be worth finding out the hard way.
    SetPageTitle(page, kTitle);

    // Before anything is rebuilt: read the controls the *previous* display left behind, so a value
    // the player changed reaches fcse.ini. Must run before the rows are replaced.
    SyncValuesFromControls();

    // Row index, not setting index. FCSE_SLOT_nn's value widget is an absolutely positioned sibling
    // sitting at the nth row's y coordinate, so a caption row consumes a slot exactly like a
    // settings row does - counting only the settings would slide every control up past its label.
    size_t row = 0;

    AppendCaption(page, L"Mods", &row);

    const std::vector<std::string>& plugins = PluginLoader::LoadedNames();
    if (plugins.empty()) {
        AppendCaption(page, L"   (no plugins installed)", &row);
        return;
    }

    for (const std::string& plugin : plugins) {
        AppendPluginBlock(page, plugin, SettingsRegistry::FindGroup(plugin), &row);
    }

    // A plugin may register under a name other than its module name, so those groups match nothing
    // above. Showing them under the name they chose beats hiding settings that exist in fcse.ini.
    for (const SettingsRegistry::Group& group : SettingsRegistry::Groups()) {
        bool alreadyShown = false;
        for (const std::string& plugin : plugins) {
            if (plugin == group.pluginName) {
                alreadyShown = true;
                break;
            }
        }
        if (!alreadyShown) {
            AppendPluginBlock(page, group.pluginName, &group, &row);
        }
    }

    Log::Loader("FcsePage: built " + std::to_string(row) + " row(s) of " +
                std::to_string(kSlotCount));
}

} // namespace FCSE
