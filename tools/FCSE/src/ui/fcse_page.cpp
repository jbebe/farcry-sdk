#include "ui/fcse_page.h"

#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "api/plugin_loader.h"
#include "api/settings_registry.h"
#include "engine/dunia_api.h"
#include "log.h"
#include "ui/engine_page_abi.h"
#include "ui/magma_package.h"
#include "ui/menu_item_handler.h"
#include "ui/page_internal.h"
#include "util/member_fn.h"
#include "util/seh.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <string>
#include <vector>
#include <windows.h>

namespace FCSE {
namespace page {

    // Engine addresses come from the address library by the Symbols::k* ids below rather than
    // baked in: the two shipped v1.03 builds place them differently. The layout these call into
    // is ui/engine_page_abi.h.
    //
    // Private to this file: everything else reaches the engine through the Safe* wrappers, which is
    // what keeps every one of these calls behind an SEH guard.
    namespace {
        GamePageCtorFn g_gamePageCtor = nullptr;
        InitFn g_init = nullptr;
        AddButtonFn g_addButton = nullptr;
        SwitchPageFn g_switchPage = nullptr;
        SetTextFn g_textBaseSetText = nullptr;
        AddBoolSettingFn g_addBoolSetting = nullptr;
        AddValueListSettingFn g_addValueListSetting = nullptr;
        AddSliderSettingFn g_addSliderSetting = nullptr;
        ElementSetVisibleFn g_elementSetVisible = nullptr;
        PageSetSelectedFn g_pageSetSelected = nullptr;
        EditBoxSetTextFn g_editBoxSetText = nullptr;
        GetUserDataElementFn g_getUserDataElement = nullptr;
        const wchar_t** g_yesText = nullptr;
        const wchar_t** g_noText = nullptr;

        bool g_installed = false;
    }

    // Shared with the other three page files - see ui/page_internal.h for who needs which.
    DisplayFn g_baseOptionPageDisplay = nullptr;
    UpdateFn g_baseUpdate = nullptr;
    const void* g_emptyStringProxy = nullptr;

    void* g_page = nullptr;
    bool g_plainRows = false;

    // FCSE's private copy of CFCXOptionGamePage's vtable. Static rather than heap-allocated because
    // the page outlives everything and a vtable that could be freed is a liability, not an asset.
    void* g_pageVtable[kPageVtableSlots];

    // Set once Init() has returned. Init itself triggers a display, and at that point the page's
    // widgets are still being bound - so the Display override chains straight to the base until the
    // page is fully built, rather than calling AddButton against half-bound state.
    bool g_pageReady = false;

    // Set when something asks for the page to be rebuilt, and acted on at the top of the next
    // Update. Deferred rather than immediate because every requester is itself running inside a walk
    // of the rows that a rebuild destroys.
    bool g_rebuildRequested = false;

    // The calls below that are more than one native touchpoint keep a wrapper of their own; the
    // plain ones go straight through SehCall. Neither may hold a C++ object with a destructor -
    // MSVC forbids mixing __try/__except with automatic unwinding in one function.

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

    // A row: label, always visible, and the handler that runs when it is activated.
    bool SafeAddButton(void* thisPtr, const wchar_t* label, void* handler, DWORD* outCode) {
        return SehCall(outCode, g_addButton, thisPtr, label, static_cast<char>(1), handler);
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
        if (!SehReadPointer(page, offset, &value, &code)) {
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
        if (!SehReadPointer(page, kTitleTextOffset, &titleText, &code) || titleText == nullptr) {
            Log::Loader("FcsePage: no title TextBase bound, stored the string only");
            return;
        }
        if (!SehCall(&code, g_textBaseSetText, titleText, title)) {
            LogFailed("magma::TextBase::SetText", code);
        }
    }

    // Opens FCSE's page from whichever Options screen the row was clicked on.
    struct NavigatePayload {
        void* ownerPage;
        void* targetPage;

        void OnActivate() {
            DWORD code = 0;
            void* gameMenu = nullptr;
            if (!SehReadPointer(ownerPage, kOwnerPageToGameMenuOffset, &gameMenu, &code)) {
                LogFailed("reading ownerPage+0x140 at click time", code);
                return;
            }
            if (gameMenu == nullptr || targetPage == nullptr || g_switchPage == nullptr) {
                Log::Loader("FcsePage: no CGameMenu* or page unavailable, click ignored");
                return;
            }

            // One page, three Options screens: rebind it to the one actually clicked before
            // switching. Both fields were written at construction time from whichever screen FCSE
            // built the page from, and the other two states own a different CGameMenu and a different
            // CFCXOptionPage - so without this, opening the page from a pause menu would drive the
            // main menu's CGameMenu, and Back would try to return to the main menu's Options screen
            // from inside a paused game.
            SehWritePointer(targetPage, kOwnerPageToGameMenuOffset, gameMenu, &code);
            SehWritePointer(targetPage, kParentPageOffset, ownerPage, &code);

            if (!SehWritePointer(gameMenu, kGameMenuNextPageOffset, targetPage, &code)) {
                LogFailed("writing CGameMenu+0x3c", code);
                return;
            }
            if (!SehCall(&code, g_switchPage, gameMenu)) {
                LogFailed("CGameMenu::SwitchPage", code);
            }
        }
    };

    // Returns the CValueListSetting the engine built for this row, so its value can be seeded now
    // and read back later. Handler is always null - see the comment at the call site.
    bool SafeAddBoolSetting(void* page, const wchar_t* label, const char* slotParam,
                            const wchar_t* yesText, const wchar_t* noText, void** outSetting,
                            DWORD* outCode) {
        return SehCallRet(outCode, outSetting, g_addBoolSetting, page, label, kLabelListParam,
                          slotParam, yesText, noText, 1, nullptr);
    }

    bool SafeAddValueListSetting(void* page, const wchar_t* label, const char* slotParam,
                                 unsigned count, const wchar_t* const* itemLabels,
                                 const unsigned* itemValues, void** outSetting, DWORD* outCode) {
        return SehCallRet(outCode, outSetting, g_addValueListSetting, page, label, kLabelListParam,
                          slotParam, count, itemLabels, itemValues, 1, nullptr);
    }

    bool SafeAddSliderSetting(void* page, const wchar_t* label, const char* slotParam, int minValue,
                              int maxValue, void** outSetting, DWORD* outCode) {
        return SehCallRet(outCode, outSetting, g_addSliderSetting, page, label, kLabelListParam,
                          slotParam, minValue, maxValue, 1, nullptr);
    }

    // The engine's cached localised strings when the player has been to the stock Game tab this
    // session, English otherwise. Never null, so the caller has nothing to check.
    const wchar_t* YesText() {
        return (g_yesText != nullptr && *g_yesText != nullptr) ? *g_yesText : kYesFallback;
    }

    const wchar_t* NoText() {
        return (g_noText != nullptr && *g_noText != nullptr) ? *g_noText : kNoFallback;
    }


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

    bool SafeReadSliderFields(void* settingObject, SliderFields* out, DWORD* outCode) {
        __try {
            auto base = reinterpret_cast<unsigned char*>(settingObject);
            out->widget = *reinterpret_cast<unsigned*>(base + 0x48);
            out->element = *reinterpret_cast<unsigned*>(base + 0x4c);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeSetElementVisible(void* element, bool visible, DWORD* outCode) {
        return SehCall(outCode, g_elementSetVisible, element, visible ? 1 : 0);
    }

    // False both for a fault and for an element the page's UserData does not name.
    bool SafeGetUserDataElement(void* userData, const NarrowString* name, void** outElement,
                                DWORD* outCode) {
        char found = 0;
        return SehCallRet(outCode, &found, g_getUserDataElement, userData, name, outElement) &&
               found != 0;
    }

    // The trailing 1 commits the value into the string beside the displayed one, which is what a
    // caller seeding a field wants.
    bool SafeEditBoxSetText(void* editBox, const WideString* text, DWORD* outCode) {
        return SehCall(outCode, g_editBoxSetText, editBox, text, static_cast<char>(1));
    }

    bool SafeSetSelected(void* magmaPage, void* focusable, DWORD* outCode) {
        return SehCall(outCode, g_pageSetSelected, magmaPage, kAnyController, focusable);
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

    std::wstring WidenAscii(const std::string& text) {
        return std::wstring(text.begin(), text.end());
    }

    bool ReadFlag(const char* key) {
        const std::string* value = SettingsRegistry::RawValue(key);
        return value != nullptr && (*value == "true" || *value == "1");
    }

    // The whole row-building sequence, shared by the Display override and by a row's click handler
    // on the plain-rows path.
    //
    // Note what is *not* here any more: the stock CFCXOptionGamePage::RefreshOptionList. FCSE used
    // to run it and then throw its eight Game-tab rows away, purely because it was also the thing
    // that bound the widgets and cached the localised YES/NO strings. It isn't - Init binds the
    // widgets, and the strings are read defensively - and running it was what populated the ten
    // option ids that made a click fatal. Now nothing but this builds our page's contents.
    void RebuildRows(void* page) {
        // Read the previous display's controls back *before* ClearSettings deletes the objects that
        // own them. Normally a no-op - the apply slot already persisted anything the player changed
        // - but it is the backstop for a change the engine did not announce.
        SyncValuesFromControls();
        LiveRows().clear();

        // Every cell off, then each row turns its own back on as it binds. Doing it this way round
        // means a row that changes type between displays - or disappears when a plugin is removed -
        // cannot leave its old control behind.
        HideAllSlotCells();

        DWORD code = 0;
        if (!SafeClearRows(page, &code)) {
            LogFailed("CSettingsPage::ClearSettings", code);
            // Fall through: appending below stale rows beats showing none at all.
        }
        FcsePage::AppendRows(page);
    }


    // Fall back to the row rendering the Mod Configuration Menu shipped with: a plain button whose
    // label carries [ON]/[OFF]. Off by default - native controls are the point of this page - but
    // kept because it asks nothing of the engine and is the thing to reach for if a native control
    // misbehaves on a build this was not tested against.
    bool PlainRows() { return ReadFlag("Plain label rows"); }

}

using namespace page;

// Builds the single private page, once per session. `optionsMenuThis` is whichever Options screen
// happened to be shown first; it supplies the initial owning CGameMenu and parent page, both of which
// NavigatePayload::OnActivate rebinds to the screen actually clicked before every switch.
static bool EnsurePage(void* optionsMenuThis) {
    if (g_installed) {
        return g_page != nullptr;
    }
    g_installed = true;
    g_plainRows = PlainRows();
    if (g_plainRows) {
        Log::Loader("FcsePage: \"Plain label rows\" is set - rendering values in the label instead "
                    "of using the engine's own controls");
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

    // Every address this page needs, checked as a set before a single one is used. The page is
    // built by calling eighteen engine functions in sequence against an object this file allocates
    // itself; discovering a missing address partway through would leave a half-constructed
    // CFCXOptionGamePage installed in the menu, which is far worse than not offering the page.
    static constexpr uint32_t kRequired[] = {
        Symbols::kGamePageCtor,        Symbols::kUiPageBaseInit,
        Symbols::kAddButton,           Symbols::kSwitchPage,
        Symbols::kTextBaseSetText,     Symbols::kAddBoolSetting,
        Symbols::kAddValueListSetting, Symbols::kAddSliderSetting,
        Symbols::kBaseOptionPageDisplay, Symbols::kBaseUpdate,
        Symbols::kElementSetVisible,   Symbols::kPageSetSelected,
        Symbols::kEditBoxSetText,      Symbols::kGetUserDataElement,
        Symbols::kEmptyStringProxy,    Symbols::kYesTextGlobal,
        Symbols::kNoTextGlobal,        Symbols::kPageVtable,
    };
    if (!AddressLibrary::ResolveAll(kRequired, sizeof(kRequired) / sizeof(kRequired[0]))) {
        Log::Loader("FcsePage: this game build is missing at least one address the settings page "
                    "needs (address mapping v" + AddressLibrary::MappingVersion() +
                    ") - not offering a page that could not be built safely");
        return false;
    }

    g_gamePageCtor = AddressLibrary::Function<GamePageCtorFn>(Symbols::kGamePageCtor);
    g_init = AddressLibrary::Function<InitFn>(Symbols::kUiPageBaseInit);
    g_addButton = AddressLibrary::Function<AddButtonFn>(Symbols::kAddButton);
    g_switchPage = AddressLibrary::Function<SwitchPageFn>(Symbols::kSwitchPage);
    g_textBaseSetText = AddressLibrary::Function<SetTextFn>(Symbols::kTextBaseSetText);
    g_addBoolSetting = AddressLibrary::Function<AddBoolSettingFn>(Symbols::kAddBoolSetting);
    g_addValueListSetting =
        AddressLibrary::Function<AddValueListSettingFn>(Symbols::kAddValueListSetting);
    g_addSliderSetting = AddressLibrary::Function<AddSliderSettingFn>(Symbols::kAddSliderSetting);
    g_baseOptionPageDisplay =
        AddressLibrary::Function<DisplayFn>(Symbols::kBaseOptionPageDisplay);
    g_baseUpdate = AddressLibrary::Function<UpdateFn>(Symbols::kBaseUpdate);
    g_elementSetVisible =
        AddressLibrary::Function<ElementSetVisibleFn>(Symbols::kElementSetVisible);
    g_pageSetSelected = AddressLibrary::Function<PageSetSelectedFn>(Symbols::kPageSetSelected);
    g_editBoxSetText = AddressLibrary::Function<EditBoxSetTextFn>(Symbols::kEditBoxSetText);
    g_getUserDataElement =
        AddressLibrary::Function<GetUserDataElementFn>(Symbols::kGetUserDataElement);
    g_emptyStringProxy =
        reinterpret_cast<const void*>(AddressLibrary::Address(Symbols::kEmptyStringProxy));
    g_yesText = reinterpret_cast<const wchar_t**>(AddressLibrary::Address(Symbols::kYesTextGlobal));
    g_noText = reinterpret_cast<const wchar_t**>(AddressLibrary::Address(Symbols::kNoTextGlobal));

    // Zero-initialized: CListMenuPage's own base-class fields (the row array and friends) are never
    // written by the ctor, and zero is what "empty row list" means.
    void* page = new unsigned char[kPageSize]();
    DWORD code = 0;
    if (!SehCall(&code, g_gamePageCtor, page)) {
        LogFailed("CFCXOptionGamePage::CFCXOptionGamePage", code);
        return false;
    }

    // Retarget the page at our own layout by overwriting the name the ctor stored. The ctor's own
    // allocator field at +0x28 is left untouched.
    OverwriteString<char, kNarrowSsoCapacity>(page, kPageNameDataOffset, kPageNameSizeOffset,
                                               kPageNameCapacityOffset, kPageName,
                                               std::strlen(kPageName));

    void* gameMenu = nullptr;
    if (SehReadPointer(optionsMenuThis, kOwnerPageToGameMenuOffset, &gameMenu, &code)) {
        SehWritePointer(page, kOwnerPageToGameMenuOffset, gameMenu, &code);
    } else {
        LogFailed("reading ownerPage+0x140", code);
    }
    SehWritePointer(page, kParentPageOffset, optionsMenuThis, &code);

    // Before Init, not after: Init triggers a display, and the stock Display is what would build the
    // Game tab's eight rows and bind their button ids into +0x1d8..+0x1fc. Taking the table over
    // first is what keeps those ids at the -1 the constructor left them at, for good.
    if (!InstallPageVtable(page, AddressLibrary::Address(Symbols::kPageVtable))) {
        Log::Loader("FcsePage: could not install the private vtable - the page would run the Game "
                    "tab's own content build and crash on the first click, so it is not offered "
                    "this session");
        return false;
    }

    if (!SehCall(&code, g_init, page)) {
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
    if (SehReadPointer(page, kBoundMagmaPageOffset, &boundPage, &code) && boundPage == nullptr) {
        Log::Loader("FcsePage: no magma::Page bound - \"FCSE_PAGE\" did not resolve through any "
                    "loaded package's GenericObjectTable, even though fcse.mgb reported loaded. "
                    "Not adding the Options row.");
        return false;
    }

    g_page = page;

    // After Init, because these resolve against the magma::Page it binds; before the first display,
    // so the very first rebuild can hide the cells it does not use.
    CacheSlotCells(page);

    g_pageReady = true; // from here on the Display override builds content rather than deferring
    SetPageTitle(page, kTitle);

    // Rows are NOT added here - our Display override clears the row list on every display, so
    // anything appended now is wiped first. AppendRows runs from inside that rebuild instead.
    //
    // The Options row is not added here either: it belongs to a particular Options screen, and there
    // are three of them. FcsePage::Install adds one per screen.
    Log::Loader("FcsePage: installed - the private page is built");
    return true;
}

bool FcsePage::Install(void* optionsMenuThis) {
    if (optionsMenuThis == nullptr) {
        Log::Loader("FcsePage: no owner page, skipping");
        return false;
    }
    if (!EnsurePage(optionsMenuThis)) {
        return false;
    }

    // One handler per Options screen, each carrying its own screen as `ownerPage`. That is what lets
    // a single page be reached from all three: OnActivate resolves the owning CGameMenu from
    // `ownerPage` at click time, so the row added to a pause menu's Options screen drives that
    // state's menu rather than the main menu's.
    DWORD code = 0;
    auto* handler = MenuItemHandler<NavigatePayload>::Create({optionsMenuThis, g_page});
    if (!SafeAddButton(optionsMenuThis, L"Mod Configuration Menu", handler, &code)) {
        LogFailed("AddButton (Options row)", code);
        return false;
    }
    Log::Loader("FcsePage: added the Mod Configuration Menu row to an Options screen");
    return true;
}

void FcsePage::AppendRows(void* page) {
    // Re-applied per display: the rebuild this runs inside may have reset the title along with the
    // rows, and it is cheap enough not to be worth finding out the hard way.
    SetPageTitle(page, kTitle);

    // The previous display's controls have already been read back and cleared by RebuildRows, which
    // has to happen before ClearSettings destroys the objects they live on.

    // Row index, not setting index. FCSE_SLOT_nn's value widget is an absolutely positioned sibling
    // sitting at the nth row's y coordinate, so a caption row consumes a slot exactly like a
    // settings row does - counting only the settings would slide every control up past its label.
    size_t row = 0;

    const std::vector<std::string>& plugins = PluginLoader::LoadedNames();

    for (const std::string& plugin : plugins) {
        AppendPluginBlock(page, plugin, SettingsRegistry::FindGroup(plugin), &row);
    }

    // A mod can reach this page without being a loaded DLL at all. LoadedNames() is plugin modules
    // only, so every Lua script's group arrives here instead - and a plugin is free to register
    // under a name other than its module name, which lands here too. Either way the group matched
    // nothing above, and showing it under the name it chose beats hiding settings that exist in
    // fcse.ini.
    //
    // This loop must run even when `plugins` is empty: returning early on "no DLLs" is what used to
    // make a script-only install look like an empty page, with the script's rows sitting in the
    // registry unread.
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

    // Nothing from either source. AppendPluginBlock always emits at least a caption per mod, so a
    // zero row count here means there is genuinely nothing installed rather than nothing configurable.
    if (row == 0) {
        AppendCaption(page, L"   (no mods installed)", &row);
    }

    Log::Loader("FcsePage: built " + std::to_string(row) + " row(s) of " +
                std::to_string(kSlotCount));
}

}
