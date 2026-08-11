#include "ui/fcse_page.h"

#include "engine/address_library.h"
#include "engine/address_symbols.h"
#include "engine/dunia_api.h"
#include "ini_file.h"
#include "log.h"
#include "ui/magma_package.h"
#include "api/plugin_loader.h"
#include "api/settings_registry.h"

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
    // Engine addresses are resolved through the address library by the Symbols::k* IDs named in the
    // comments below, not baked in: the two shipped v1.03 builds place them differently, and the
    // hex quoted alongside each name is only correct on fc2_103_uplay (Steam/Uplay). The prose is
    // kept because it is the record of how each was identified.

    // CFCXOptionGamePage's real ctor, and the allocation size AddPage<CFCXOptionGamePage> uses.
    //
    // Deliberately the *concrete leaf*, not the tidier-looking CFCXBaseOptionPage one class below
    // it (0x1087ec80). That base was tried on 2026-08-08 and is **abstract**: constructing it works
    // and the page displays, but Back kills the process with R6025 "pure virtual function call".
    // Its content-build slot at vtable+0x3c being a plain RET says nothing about the rest of the
    // table; do not read that as "concrete" again.
    // Symbols::kGamePageCtor (0x1081e9c0 on fc2_103_uplay).
    constexpr size_t kPageSize = 0x210;

    // CUIPageBase::Init - turns the page's authored name into a bound magma::Page and its widgets.
    // Nothing in the engine calls it implicitly, which is why every earlier hand-built page
    // displayed nothing and then faulted.
    // Symbols::kUiPageBaseInit (0x10109410 on fc2_103_uplay).
    // Symbols::kAddButton (0x10cdbb80 on fc2_103_uplay).
    // Symbols::kSwitchPage (0x101d1990 on fc2_103_uplay). // CFCXOptionGamePage's primary vtable, and the three slots FCSE replaces in its own private
    // copy of it. This is what replaced the old global hook on CFCXOptionGamePage::RefreshOptionList
    // (0x10820160), and the reason is worth stating: everything about this class that is specific to
    // the *Game tab* hangs off exactly these three slots, and nothing reaches it any other way.
    //
    //   +0x08  Display          0x108211c0  { RefreshOptionList(); this+0x200 = 0; base::Display(); }
    //   +0x10  Update(float)    0x10820100  base update, then a switch on this+0x200
    //   +0x4c  OnSettingChanged 0x10820150  -> FUN_1081f6c0
    //   +0x50  apply            0x108200d0  -> CFCXOptionGamePage::ApplyOptionsFromSettings  (0x1081fd10)
    //   +0x54  refresh          0x108200e0  -> CFCXOptionGamePage::UpdateSettingsFromOptions (0x1081f800)
    //
    // That list is not a guess and not a sample. Scanning this class's whole translation unit for
    // instructions that form the address of anything in the id block found 24 of them, in exactly
    // five functions - RefreshOptionList, FUN_1081f4f0, FUN_1081f6c0, and the apply/refresh pair -
    // and each of those five is reachable only through one of the five slots above. Replace the
    // five and nothing can touch the ids at all. An earlier version of this code replaced only
    // three, and the two it missed were found the way such things always are: the game crashed.
    //
    // The last two are why the native-control path used to crash on a click. The page stores ten
    // *button ids* at +0x1d8..+0x1fc, one per Game-tab option, and both functions do:
    //
    //     setting = m_settings[id];                     // the map at page+0x190
    //     if (setting && !IsKindOf(setting, Expected))
    //         setting = nullptr;                        // the guard nulls it...
    //     value = setting->vtable[14](setting);         // ...and the call dereferences it anyway
    //
    // On the stock Game tab the ids always resolve to a setting of the expected type, so the shipped
    // bug never fires. FCSE's page cleared the native rows and appended its own, which were handed
    // the same button ids back with the wrong types - a bool value-list where SETTING_SENSITIVITY
    // expects a slider - so the type check failed and the null was dereferenced. Resetting the ids
    // to -1 is not a fix either: std::map::operator[] inserts a null for a missing key and the same
    // dereference follows.
    //
    // Owning a copy of the table fixes all of it at once and is strictly better than hooking:
    // RefreshOptionList never runs on our page, so the ids stay -1 forever, no native settings are
    // built and destroyed per display, the three observer objects RefreshOptionList allocates and
    // registers on every display are never created - and the stock Game tab is untouched *by
    // construction*, because it still points at the engine's table. Nothing in the process is
    // patched, so FCSE also stops competing with any other mod that hooks these functions.
    //
    // 26 slots is exact, not a guess: 0x10eada40 is not a 27th entry, it is the
    // "MAINMENU_OPTIONGAME_PAGE_PC" string the constructor pushes.
    // Symbols::kPageVtable (0x10ead9d8 on fc2_103_uplay).
    constexpr size_t kPageVtableSlots = 26;
    constexpr size_t kDisplaySlot = 2;        // +0x08
    constexpr size_t kUpdateSlot = 4;         // +0x10
    constexpr size_t kSettingChangedSlot = 19; // +0x4c
    constexpr size_t kApplySlot = 20;         // +0x50
    constexpr size_t kRefreshSlot = 21;       // +0x54

    // What our Display chains to once it has built its own rows - the rest of the stock Display,
    // minus the RefreshOptionList call that opens it.
    // Symbols::kBaseOptionPageDisplay (0x1087ed50 on fc2_103_uplay). // What the stock Update calls before its own state machine - the base class's per-frame tick,
    // which is the only part of that slot a page like ours still wants.
    // Symbols::kBaseUpdate (0x10108c10 on fc2_103_uplay). // Cleared by the stock Display on every display; mirrored so ours behaves identically.
    constexpr ptrdiff_t kDisplayResetFieldOffset = 0x200;

    // CSettingsPage::ClearSettings (0x10cddf20) - the engine's own "drop my rows and delete their
    // settings". The stock RefreshOptionList calls it before building anything; ours does the same.
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
    // Symbols::kAddBoolSetting (0x10cde0d0 on fc2_103_uplay). // CSettingsPage::AddValueListSetting<unsigned> - the N-option form of the same row, and what a
    // Choice setting renders as. The Game tab's Difficulty (4 options) and Machete (3) rows are this.
    //
    //   AddValueListSetting(this, label, labelListParam, settingParam, count, itemLabels,
    //                       itemValues, enabled, handler)
    //
    // No new layout is needed for it: common.mgb #652FD37C, the cell FCSE_SLOT_nn already links, is a
    // ListBox with BUTTONCOUNT=1 - a one-item viewport that scrolls through however many items were
    // added. Two items is a YES/NO toggle, four is a dropdown; it is the same widget either way.
    //
    // itemLabels is an array of plain wchar_t*, not of engine string objects: the stock caller fills
    // it from Oasis::GetLocalizedString, which returns exactly that. So our own strings work, as long
    // as they outlive the row - see LabelStorage.
    // Symbols::kAddValueListSetting (0x1081d660 on fc2_103_uplay). // CSettingsPage::AddSliderSetting - a label plus a draggable slider, bound through the second
    // slot bank (FCSE_SLIDER_nn -> common.mgb #62EA6603) rather than the value cell.
    //
    //   AddSliderSetting(this, label, labelListParam, settingParam, min, max, enabled, handler)
    //
    // The value is an int in both directions: CSliderSetting::SetValue (0x10cde270) forwards it to
    // magma::Slider::SetValue, which takes an int and converts to float itself, and GetValue
    // (0x10cde240) reads the widget's float back, adds 0.5 and truncates. So the same vtable slots
    // 13/14 the other settings use work here unchanged.
    // Symbols::kAddSliderSetting (0x10cddff0 on fc2_103_uplay). // magma::UserData::GetUserDataElement(const std::string& name, Element*& out) - resolves one of
    // the page area's FullLink properties to the element it names, which is exactly how the engine
    // finds FCSE_SLOT_nn itself (CUISettingBase::FetchMagmaElements calls this).
    //
    // Used to get all 40 cell elements up front so the unused ones can be hidden. Without it a cell
    // is only reachable by binding a setting to it, which is the one thing we do not want to do to
    // a row that is not using it.
    // Symbols::kGetUserDataElement (0x10a963a0 on fc2_103_uplay). // magma::Element::SetVisible(bool) - bit 0 of the flags byte at element+0x34, and the same call
    // the dispatcher makes for ShowElementNomad / HideElementNomad. Safe in this direction: the
    // cells are authored visible, so their sub-areas exist and only the draw flag moves. (Going the
    // other way - authored HIDDEN, revealed by code - is bit 1 and does not work; see the note by
    // the slot-cell comment.)
    // Symbols::kElementSetVisible (0x10ab13f0 on fc2_103_uplay). // The empty-string constant this build's std::string points its proxy field at. Copied so a
    // forged string looks exactly like one the engine made.
    // Symbols::kEmptyStringProxy (0x10fd42d1 on fc2_103_uplay). // CFCXBaseOptionPage's "the player changed something and has not applied it" flag.
    //
    // CFCXBaseOptionPage::SetDirty (0x1087eb50) sets it when a row changes; ApplyIfDirty
    // (0x1087eb10) calls vtable +0x50 and clears it; the Back path reads it and puts up the
    // "you have unsaved changes" prompt. That prompt is right for a stock options page, which
    // batches edits until Apply - and wrong for this one, which writes fcse.ini on the change
    // itself. So FCSE clears the flag rather than answering the question: there is genuinely
    // nothing pending.
    constexpr ptrdiff_t kDirtyFlagOffset = 0x1b8;

    // magma::Page::SetSelected(int controller, Focusable* element) - moves input focus to an
    // element. This is exactly what the dispatcher does for the SetFocusNomad action: resolve the
    // target and call this on the owning page. Needed because an EditBox authored beside the row
    // list has no NEIGHBORS, so nothing routes focus into it on its own.
    //
    // 255 is the "any controller" value the layout's own DEFAULT_ELEMENT uses. The engine's own call
    // site passes the real main-controller id (from 0x104fe5a0) instead; if 255 turns out not to
    // take, that is the next thing to try.
    // magma::EditBox::SetText(const std::wstring&, bool) - the EditBox's own setter, not
    // TextBase's. An EditBox derives from Widget, so magma::TextBase::SetText writes through the
    // wrong layout: that was tried, and it corrupted the widget badly enough that the CRT faulted
    // and then magma's draw pass did.
    //
    // The engine clamps to the layout's maxLength itself (a u16 at widget+0x18), and the trailing
    // bool copies the value into the committed string beside the displayed one - which is what a
    // caller seeding a field wants, so FCSE passes true. It ends by marking the text dirty, which is
    // what actually makes the field re-render; writing the string in memory would not have.
    // Symbols::kEditBoxSetText (0x10ab0220 on fc2_103_uplay).
    // Symbols::kPageSetSelected (0x10aa5180 on fc2_103_uplay).
    constexpr int kAnyController = 255;

    // The localised "YES"/"NO" strings, as raw wchar_t*. RefreshOptionList fills these lazily, once
    // per process, guarded by bits 1 and 2 of the flag word at 0x1164fca8 - so now that FCSE's page
    // no longer runs RefreshOptionList, they are only populated if the player has opened the stock
    // Game tab at least once. Both are read defensively and fall back to English literals.
    //
    // Doing it properly means calling the OASIS lookup directly. Its shape is recovered and recorded
    // here so it can be picked up without re-deriving it:
    //
    //   const wchar_t* __thiscall Oasis::GetLocalizedString(   // 0x104d1e40
    //       void* self,                    // *(void**)0x11644778
    //       const std::string& category,   // "Generic"
    //       const std::string& key,        // "YES" / "NO"
    //       void* extra);                  // *(void**)0x10f9d874
    //
    // with std::string laid out as { void* proxy; char sso[16]; uint32 size; uint32 capacity; } -
    // the function reads capacity at +0x18, and takes the characters from +0x04 when it is < 0x10.
    // Left undone because it is 40 lines of string marshalling for a cosmetic gain, and every other
    // label on this page is plugin-supplied English anyway.
    // Symbols::kYesTextGlobal (0x1164fca4 on fc2_103_uplay).
    // Symbols::kNoTextGlobal (0x1164fca0 on fc2_103_uplay).
    constexpr wchar_t kYesFallback[] = L"YES";
    constexpr wchar_t kNoFallback[] = L"NO";

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
    // Symbols::kTextBaseSetText (0x1007d770 on fc2_103_uplay). // CUIPageBase's page-name std::string. The object starts at +0x28 with its embedded allocator;
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
    using DisplayFn = void(__thiscall*)(void* thisPtr);
    using UpdateFn = void(__thiscall*)(void* thisPtr, float deltaTime);
    using ElementSetVisibleFn = void(__thiscall*)(void* element, int visible);
    using PageSetSelectedFn = void(__thiscall*)(void* magmaPage, int controller, void* focusable);

    // MSVC's std::wstring as this build lays it out - the same shape fcse_page's page-title writer
    // already relies on: proxy pointer, 8 inline wchar_t while capacity is under 8, size, capacity.
    struct WideString {
        const void* proxy;
        wchar_t buffer[8];
        uint32_t size;
        uint32_t capacity;
    };

    using EditBoxSetTextFn = void(__thiscall*)(void* editBox, const WideString* text, char commit);

    // MSVC's std::string as this build lays it out: an allocator/proxy pointer, then a 16-byte
    // buffer holding the characters inline while capacity is under 0x10, then size, then capacity.
    // Confirmed from two directions - it is what the UserData getter reads (capacity at +0x18,
    // characters at +0x04) and what CFCXOptionGamePage's own constructor builds for its page name.
    struct NarrowString {
        const void* proxy;
        char buffer[16];
        uint32_t size;
        uint32_t capacity;
    };

    using GetUserDataElementFn = char(__thiscall*)(void* userData, const NarrowString* name,
                                                   void** outElement);
    using ClearRowsFn = void(__thiscall*)(void* thisPtr);
    using AddBoolSettingFn = void*(__thiscall*)(void* page, const wchar_t* label,
                                                const char* labelListParam, const char* settingParam,
                                                const wchar_t* yesText, const wchar_t* noText,
                                                int enabled, void* handler);
    using AddValueListSettingFn = void*(__thiscall*)(void* page, const wchar_t* label,
                                                     const char* labelListParam,
                                                     const char* settingParam, unsigned count,
                                                     const wchar_t* const* itemLabels,
                                                     const unsigned* itemValues, int enabled,
                                                     void* handler);
    using AddSliderSettingFn = void*(__thiscall*)(void* page, const wchar_t* label,
                                                  const char* labelListParam,
                                                  const char* settingParam, int minValue,
                                                  int maxValue, int enabled, void* handler);

    GamePageCtorFn g_gamePageCtor = nullptr;
    InitFn g_init = nullptr;
    AddButtonFn g_addButton = nullptr;
    SwitchPageFn g_switchPage = nullptr;
    SetTextFn g_textBaseSetText = nullptr;
    DisplayFn g_baseOptionPageDisplay = nullptr;
    UpdateFn g_baseUpdate = nullptr;
    AddBoolSettingFn g_addBoolSetting = nullptr;
    AddValueListSettingFn g_addValueListSetting = nullptr;
    AddSliderSettingFn g_addSliderSetting = nullptr;
    ElementSetVisibleFn g_elementSetVisible = nullptr;
    PageSetSelectedFn g_pageSetSelected = nullptr;
    EditBoxSetTextFn g_editBoxSetText = nullptr;
    GetUserDataElementFn g_getUserDataElement = nullptr;
    const void* g_emptyStringProxy = nullptr;
    const wchar_t** g_yesText = nullptr;
    const wchar_t** g_noText = nullptr;

    void* g_page = nullptr;
    bool g_installed = false;
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

    bool SafeBaseDisplay(void* page, DWORD* outCode) {
        __try {
            g_baseOptionPageDisplay(page);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeBaseUpdate(void* page, float deltaTime, DWORD* outCode) {
        __try {
            g_baseUpdate(page, deltaTime);
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

            // One page, three Options screens: rebind it to the one actually clicked before
            // switching. Both fields were written at construction time from whichever screen FCSE
            // built the page from, and the other two states own a different CGameMenu and a different
            // CFCXOptionPage - so without this, opening the page from a pause menu would drive the
            // main menu's CGameMenu, and Back would try to return to the main menu's Options screen
            // from inside a paused game.
            SafeWritePointer(targetPage, kOwnerPageToGameMenuOffset, gameMenu, &code);
            SafeWritePointer(targetPage, kParentPageOffset, ownerPage, &code);

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

    // Wrapped for the same reason ToggleCheckbox is: this runs from inside the engine's own apply
    // path, and it writes fcse.ini, so a fault anywhere under it would unwind through engine code.
    bool SafeSetValue(SettingsRegistry::Setting* setting, const FCSE_SettingValue& next,
                      DWORD* outCode) {
        __try {
            SettingsRegistry::SetValue(setting, next);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // Returns the CValueListSetting the engine built for this row, so its value can be seeded now
    // and read back later. Handler is always null - see the comment at the call site.
    bool SafeAddBoolSetting(void* page, const wchar_t* label, const char* slotParam,
                            const wchar_t* yesText, const wchar_t* noText, void** outSetting,
                            DWORD* outCode) {
        __try {
            *outSetting = g_addBoolSetting(page, label, kLabelListParam, slotParam, yesText, noText,
                                           1, nullptr);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeAddValueListSetting(void* page, const wchar_t* label, const char* slotParam,
                                 unsigned count, const wchar_t* const* itemLabels,
                                 const unsigned* itemValues, void** outSetting, DWORD* outCode) {
        __try {
            *outSetting = g_addValueListSetting(page, label, kLabelListParam, slotParam, count,
                                                itemLabels, itemValues, 1, nullptr);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeAddSliderSetting(void* page, const wchar_t* label, const char* slotParam, int minValue,
                              int maxValue, void** outSetting, DWORD* outCode) {
        __try {
            *outSetting = g_addSliderSetting(page, label, kLabelListParam, slotParam, minValue,
                                             maxValue, 1, nullptr);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    // The engine's cached localised strings when the player has been to the stock Game tab this
    // session, English otherwise. Never null, so the caller has nothing to check.
    const wchar_t* YesText() {
        return (g_yesText != nullptr && *g_yesText != nullptr) ? *g_yesText : kYesFallback;
    }

    const wchar_t* NoText() {
        return (g_noText != nullptr && *g_noText != nullptr) ? *g_noText : kNoFallback;
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

    // The same two slots on the 4-byte-valued settings: CValueListSetting<unsigned> for a Choice
    // row, CSliderSetting for a Slider one. Separate typedefs rather than a shared one over void*,
    // because the width matters in both directions - the bool form points at a single byte, and
    // reading four from it would run off the end of the engine's value array.
    //
    // Signedness does not: a Choice index is never negative and a slider's range is whatever the
    // plugin declared, so the caller decides which way to read the same four bytes.
    using SettingSetValueDwordFn = void(__thiscall*)(void* self, const uint32_t* value);
    using SettingGetValueDwordFn = const uint32_t*(__thiscall*)(void* self);

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

    bool SafeSetSettingValueDword(void* settingObject, uint32_t value, DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(settingObject);
            auto setValue = reinterpret_cast<SettingSetValueDwordFn>(vtable[kSettingSetValueSlot]);
            setValue(settingObject, &value);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeGetSettingValueDword(void* settingObject, uint32_t* outValue, DWORD* outCode) {
        __try {
            void** vtable = *reinterpret_cast<void***>(settingObject);
            auto getValue = reinterpret_cast<SettingGetValueDwordFn>(vtable[kSettingGetValueSlot]);
            const uint32_t* value = getValue(settingObject);
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

    // The fields CSliderSetting::FetchMagmaElements (0x10cde3c0) writes, which are NOT the ones the
    // value-list variant uses: the element lands at +0x4c and the widget at +0x48, where a
    // CValueListSetting puts them at +0x48 and +0x44. A slider has no value array either, so the
    // widget being non-null is the whole test that the slot resolved.
    struct SliderFields {
        unsigned widget;  // +0x48
        unsigned element; // +0x4c
    };

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
        __try {
            g_elementSetVisible(element, visible ? 1 : 0);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeGetUserDataElement(void* userData, const NarrowString* name, void** outElement,
                                DWORD* outCode) {
        __try {
            return g_getUserDataElement(userData, name, outElement) != 0;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeEditBoxSetText(void* editBox, const WideString* text, DWORD* outCode) {
        __try {
            g_editBoxSetText(editBox, text, 1);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeSetSelected(void* magmaPage, void* focusable, DWORD* outCode) {
        __try {
            g_pageSetSelected(magmaPage, kAnyController, focusable);
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeClearDirtyFlag(void* page, DWORD* outCode) {
        __try {
            *(reinterpret_cast<unsigned char*>(page) + kDirtyFlagOffset) = 0;
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

    void RebuildRows(void* page); // defined below, next to the Display override
    void BindEditCell(SettingsRegistry::Setting* setting, size_t row); // defined with the slot cells
    // Which of the three banks a row's control lives in. Defined up here because the row handlers
    // below need it before the cache itself is declared.
    enum class CellKind { Value, Slider, Edit };
    void* SlotCellElement(size_t row, CellKind kind);

    // The click handler for toggle rows: flip the value, then rebuild the page so the row's
    // [ON]/[OFF] reflects it immediately.
    //
    // Only reachable on the "Plain label rows" fallback path - a native checkbox row carries a
    // CValueListSetting and no button handler at all.
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

    // Activating a Text row hands input focus to its EditBox, which is the only way in: the field is
    // authored beside the row list with empty NEIGHBORS, so no amount of arrow-key navigation
    // reaches it. This is the same move the engine makes for the SetFocusNomad action - resolve the
    // target element and call magma::Page::SetSelected on the owning page.
    struct FocusHandler {
        void** vtable; // must stay first
        size_t row;

        unsigned int SafeNoOp(unsigned int /*arg*/) { return 0; }

        unsigned int OnActivate(unsigned int /*arg*/) {
            void* element = SlotCellElement(row, CellKind::Edit);
            DWORD code = 0;
            void* magmaPage = nullptr;
            if (element == nullptr || g_page == nullptr ||
                !SafeReadPointer(g_page, kBoundMagmaPageOffset, &magmaPage, &code) ||
                magmaPage == nullptr) {
                return 0;
            }
            if (!SafeSetSelected(magmaPage, element, &code)) {
                LogFailed("magma::Page::SetSelected", code);
            }
            return 0;
        }

        static FocusHandler* Create(size_t row);
    };

    using FocusHandlerMemberFn = unsigned int (FocusHandler::*)(unsigned int);

    void* RawFocusHandlerPointer(FocusHandlerMemberFn fn) {
        union {
            FocusHandlerMemberFn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    void* g_focusVtable[kVtableSlotCount];
    bool g_focusVtableReady = false;

    FocusHandler* FocusHandler::Create(size_t row) {
        if (!g_focusVtableReady) {
            void* noOp = RawFocusHandlerPointer(&FocusHandler::SafeNoOp);
            for (void*& slot : g_focusVtable) {
                slot = noOp;
            }
            g_focusVtable[kActivateSlot] = RawFocusHandlerPointer(&FocusHandler::OnActivate);
            g_focusVtableReady = true;
        }
        auto* handler = new FocusHandler();
        handler->vtable = g_focusVtable;
        handler->row = row;
        return handler;
    }

    // A Text row: a label, and an EditBox cell the player types into.
    //
    // The row itself carries no handler and no CUISettingBase - there is no "string setting" in
    // CSettingsPage - so the label is a plain button and the editing happens entirely in the cell.
    // This mirrors the stock Options > Network page, which authors bare EditBox elements at its row
    // positions rather than routing text through a dialog.
    bool AppendTextRow(void* page, SettingsRegistry::Setting* setting, size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));
        DWORD code = 0;
        if (!SafeAddButton(page, LabelStorage().back().c_str(), FocusHandler::Create(row), &code)) {
            LogFailed("AddButton (text row)", code);
            return false;
        }
        BindEditCell(setting, row);
        return true;
    }

    // NOTE: the native AddBoolSetting path below has no handler of its own. An earlier version made
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

        // Text rows only: the EditBox's committed string as it was when the row was built. The
        // engine copies the live text into it when the player presses Enter and at no other time,
        // so a change here is precisely an Enter - which is what the stock Network page treats as
        // "commit and refresh the page".
        std::wstring committed;
    };

    std::vector<LiveRow>& LiveRows() {
        static std::vector<LiveRow> rows;
        return rows;
    }

    // Every cell of both banks, resolved once and cached, so the ones a display does not use can be
    // hidden. Indexed by row; null for anything that did not resolve.
    //
    // The layout authors 20 value cells and 20 slider cells at the *same* twenty row positions,
    // because a row's type is not known until a plugin registers. Without this, all forty draw at
    // once and every row shows a slider sitting on top of a spinner.
    //
    // Both banks are authored visible, and this only ever moves bit 0 of element+0x34 - the same bit
    // ShowElementNomad and HideElementNomad move. The opposite arrangement (author HIDDEN, reveal
    // from code) was tried and does not work: `HIDDEN` is bit *1* of that byte, magma's draw
    // collection skips any element with bit 1 set (`(flags & 2) == 0`, at 0x10ad3fb0), and
    // SetVisible cannot clear it - so the cell was never drawn and the engine dereferenced a null
    // the frame after it was "shown".
    struct SlotCells {
        void* value[kSlotCount];
        void* slider[kSlotCount];
        void* edit[kSlotCount];
    };

    SlotCells g_slotCells{};
    bool g_slotCellsCached = false;

    bool MakeNarrowString(NarrowString* out, const char* text) {
        size_t length = std::strlen(text);
        if (length >= sizeof(out->buffer)) {
            return false; // every slot name is well inside the SSO buffer; nothing else is passed
        }
        std::memset(out, 0, sizeof(*out));
        out->proxy = g_emptyStringProxy;
        std::memcpy(out->buffer, text, length + 1);
        out->size = static_cast<uint32_t>(length);
        out->capacity = static_cast<uint32_t>(sizeof(out->buffer) - 1);
        return true;
    }

    void* ResolveSlotCell(void* magmaPage, const char* slotParam) {
        NarrowString name{};
        if (!MakeNarrowString(&name, slotParam)) {
            return nullptr;
        }
        void* element = nullptr;
        DWORD code = 0;
        if (!SafeGetUserDataElement(magmaPage, &name, &element, &code)) {
            if (code != 0) {
                LogFailed("magma::UserData::GetUserDataElement", code);
            }
            return nullptr;
        }
        return element;
    }

    // Resolves all forty cells against the page's own UserData. Runs once, after Init has bound the
    // magma page - before that there is nothing to resolve against.
    void CacheSlotCells(void* page) {
        DWORD code = 0;
        void* magmaPage = nullptr;
        if (!SafeReadPointer(page, kBoundMagmaPageOffset, &magmaPage, &code) ||
            magmaPage == nullptr) {
            Log::Loader("FcsePage: no magma::Page to resolve slot cells against - unused cells will "
                        "stay on screen");
            return;
        }

        size_t resolved = 0;
        for (size_t i = 0; i < kSlotCount; ++i) {
            char slotParam[24];
            sprintf_s(slotParam, "FCSE_SLOT_%02zu", i + 1);
            g_slotCells.value[i] = ResolveSlotCell(magmaPage, slotParam);
            sprintf_s(slotParam, "FCSE_SLIDER_%02zu", i + 1);
            g_slotCells.slider[i] = ResolveSlotCell(magmaPage, slotParam);
            sprintf_s(slotParam, "FCSE_EDIT_%02zu", i + 1);
            g_slotCells.edit[i] = ResolveSlotCell(magmaPage, slotParam);
            resolved += (g_slotCells.value[i] != nullptr) + (g_slotCells.slider[i] != nullptr) +
                        (g_slotCells.edit[i] != nullptr);
        }
        g_slotCellsCached = true;
        Log::Loader("FcsePage: resolved " + std::to_string(resolved) + " of " +
                    std::to_string(kSlotCount * 3) +
                    " slot cells - the unused ones are hidden per display");

        // Called out because it is the one link shape nothing in the shipped corpus uses: the text
        // fields are bare EditBox elements, so their links are a 3-id chain rather than the 5-id
        // through-an-instance form. If they did not resolve, that is why, and the fix is to wrap the
        // EditBox in a local area and instance it like the other two banks.
        if (g_slotCells.edit[0] == nullptr) {
            Log::Loader("FcsePage: FCSE_EDIT_01 did not resolve - the engine does not accept a "
                        "3-id FullLink to a bare element. Text rows will show their value but not "
                        "be editable.");
        }
    }

    void HideAllSlotCells() {
        if (!g_slotCellsCached) {
            return;
        }
        for (size_t i = 0; i < kSlotCount; ++i) {
            DWORD code = 0;
            if (g_slotCells.value[i] != nullptr) {
                SafeSetElementVisible(g_slotCells.value[i], false, &code);
            }
            if (g_slotCells.slider[i] != nullptr) {
                SafeSetElementVisible(g_slotCells.slider[i], false, &code);
            }
            if (g_slotCells.edit[i] != nullptr) {
                SafeSetElementVisible(g_slotCells.edit[i], false, &code);
            }
        }
    }

    void* SlotCellElement(size_t row, CellKind kind) {
        if (!g_slotCellsCached || row >= kSlotCount) {
            return nullptr;
        }
        switch (kind) {
        case CellKind::Value:
            return g_slotCells.value[row];
        case CellKind::Slider:
            return g_slotCells.slider[row];
        case CellKind::Edit:
            return g_slotCells.edit[row];
        }
        return nullptr;
    }

    // `row` is the zero-based row index, which is also the slot index - see AppendPluginBlock.
    void ShowSlotCell(size_t row, CellKind kind) {
        void* element = SlotCellElement(row, kind);
        if (element == nullptr) {
            return;
        }
        DWORD code = 0;
        if (!SafeSetElementVisible(element, true, &code)) {
            LogFailed("magma::Element::SetVisible(true)", code);
        }
    }

    // An EditBox holds its text as a std::wstring at widget+0x8c - the same offset the shipped
    // listener at 0x108fe850 reads out of the message-box edit box. Read into a fixed buffer inside
    // the guard, because nothing with a destructor may live in a function that uses __try.
    constexpr ptrdiff_t kElementWidgetOffset = 0x14;

    // A magma::EditBox keeps several std::wstrings. Two matter, and both offsets are *observed*, not
    // derived: a probe that scanned the widget for string-shaped members while a player typed showed
    //
    //     +0x8C="kilimanjaro"   +0xA8="kilimanjaro"
    //     +0x8C="kilimanjaroa"  +0xA8="kilimanjaro"
    //
    // so +0x8c is the live text the player is editing and +0xa8 is the committed copy.
    //
    // Worth stating because it was got wrong twice. The server build lays these out as three
    // consecutive strings, and it is tempting to convert an ELF offset by scaling for MSVC's 0x1c
    // -byte wstring - that gives +0x70 here, and it is wrong, because the surrounding sub-objects
    // hold strings too and therefore differ in size by different amounts. Nothing about that
    // mistake is loud: +0x70 reads as a perfectly valid empty string, so the symptom was a setting
    // that silently saved as blank rather than anything that faulted.
    constexpr ptrdiff_t kEditBoxDisplayedTextOffset = 0x8c;
    constexpr ptrdiff_t kEditBoxCommittedTextOffset = 0xa8;
    constexpr size_t kEditTextMax = 64;

    // Reads one candidate std::wstring at `offset` into `out`, reporting whether it looks like a
    // string at all. Used by the probe below, which exists because the displayed-text offset was
    // derived arithmetically from the committed one rather than observed - and a wrong offset here
    // fails silently, returning a stale value instead of faulting.
    bool SafeReadStringAt(void* widget, ptrdiff_t offset, wchar_t* out, size_t max, DWORD* outCode) {
        __try {
            auto text = reinterpret_cast<const char*>(widget) + offset;
            uint32_t size = *reinterpret_cast<const uint32_t*>(text + 0x14);
            uint32_t capacity = *reinterpret_cast<const uint32_t*>(text + 0x18);
            // What an MSVC wstring always satisfies, and arbitrary memory rarely does.
            if (capacity < 7 || size > capacity || capacity > 0x10000) {
                return false;
            }
            const wchar_t* characters = capacity < 8
                                             ? reinterpret_cast<const wchar_t*>(text + 4)
                                             : *reinterpret_cast<const wchar_t* const*>(text + 4);
            if (characters == nullptr) {
                return false;
            }
            if (size >= max) {
                size = static_cast<uint32_t>(max) - 1;
            }
            for (uint32_t i = 0; i < size; ++i) {
                out[i] = characters[i];
            }
            out[size] = L'\0';
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            *outCode = GetExceptionCode();
            return false;
        }
    }

    bool SafeReadEditText(void* widget, wchar_t* out, DWORD* outCode) {
        return SafeReadStringAt(widget, kEditBoxDisplayedTextOffset, out, kEditTextMax, outCode);
    }

    // Seeds a Text row's field from the registry and reveals it. The widget hangs off the element at
    // +0x14, same as every other magma widget.
    void BindEditCell(SettingsRegistry::Setting* setting, size_t row) {
        void* element = SlotCellElement(row, CellKind::Edit);
        if (element == nullptr) {
            return; // logged once at cache time
        }

        DWORD code = 0;
        void* widget = nullptr;
        if (!SafeReadPointer(element, kElementWidgetOffset, &widget, &code) || widget == nullptr) {
            Log::Loader("FcsePage: text field for row " + std::to_string(row + 1) +
                        " has no widget - leaving it hidden");
            return;
        }

        // Seeded through the EditBox's own setter. The first attempt used
        // magma::TextBase::SetText and took the game down twice over - an EditBox derives from
        // Widget, not TextBase, so that wrote through the wrong layout and the fault surfaced once
        // in the CRT's string code and once in magma's draw pass.
        WideString seed{};
        std::wstring wide = WidenAscii(setting->text);
        if (wide.size() < 8) {
            seed.proxy = g_emptyStringProxy;
            std::memcpy(seed.buffer, wide.c_str(), (wide.size() + 1) * sizeof(wchar_t));
            seed.size = static_cast<uint32_t>(wide.size());
            seed.capacity = 7;
        } else {
            // Longer than the inline buffer, so the string has to point at a heap block. Leaked
            // deliberately: the engine copies out of it and never owns it, and a settings value is a
            // few bytes once per display.
            auto* buffer = new wchar_t[wide.size() + 1];
            std::memcpy(buffer, wide.c_str(), (wide.size() + 1) * sizeof(wchar_t));
            seed.proxy = g_emptyStringProxy;
            *reinterpret_cast<wchar_t**>(&seed.buffer[0]) = buffer;
            seed.size = static_cast<uint32_t>(wide.size());
            seed.capacity = static_cast<uint32_t>(wide.size());
        }
        if (!SafeEditBoxSetText(widget, &seed, &code)) {
            LogFailed("magma::EditBox::SetText", code);
        }

        ShowSlotCell(row, CellKind::Edit);

        wchar_t committed[kEditTextMax] = {};
        DWORD probeCode = 0;
        SafeReadStringAt(widget, kEditBoxCommittedTextOffset, committed, kEditTextMax, &probeCode);
        LiveRows().push_back({setting, widget, committed});
    }

    // Persistence, without a click handler. The engine owns the control and changes it in place;
    // FCSE reads it back and writes fcse.ini for anything that moved.
    //
    // Called from two places, which is why it does not clear LiveRows() itself: from the apply slot
    // (+0x50), where the rows are still on screen and the player may change another one a moment
    // later, and from the start of a rebuild, which clears the list itself once the old controls
    // have been read. Clearing here would make the apply slot a one-shot per display.
    //
    // Idempotent by construction - a row whose control still matches the registry is skipped - so
    // being called twice for the same change costs nothing.
    void SyncValuesFromControls() {
        for (const LiveRow& row : LiveRows()) {
            DWORD code = 0;
            FCSE_SettingValue shown{};
            shown.type = row.setting->value.type;

            switch (row.setting->value.type) {
            case FCSE_SettingType_Checkbox: {
                bool value = false;
                if (!SafeGetSettingValue(row.settingObject, &value, &code)) {
                    if (code != 0) {
                        LogFailed("CValueListSetting<bool>::GetValue", code);
                    }
                    continue;
                }
                shown.asNumber = value ? 1 : 0;
                break;
            }
            case FCSE_SettingType_Choice: {
                uint32_t value = 0;
                if (!SafeGetSettingValueDword(row.settingObject, &value, &code)) {
                    if (code != 0) {
                        LogFailed("CValueListSetting<unsigned>::GetValue", code);
                    }
                    continue;
                }
                shown.asChoice = value;
                break;
            }
            case FCSE_SettingType_Slider: {
                uint32_t value = 0;
                if (!SafeGetSettingValueDword(row.settingObject, &value, &code)) {
                    if (code != 0) {
                        LogFailed("CSliderSetting::GetValue", code);
                    }
                    continue;
                }
                shown.asSlider = static_cast<int32_t>(value);
                break;
            }
            case FCSE_SettingType_Text: {
                // For a Text row the "setting object" is the EditBox widget itself: a string has no
                // CUISettingBase, so BindEditCell records the widget here instead.
                wchar_t typed[kEditTextMax] = {};
                if (!SafeReadEditText(row.settingObject, typed, &code)) {
                    if (code != 0) {
                        LogFailed("reading an EditBox's text", code);
                    }
                    continue;
                }
                std::string narrowed;
                for (const wchar_t* c = typed; *c != L'\0'; ++c) {
                    narrowed.push_back(*c < 0x100 ? static_cast<char>(*c) : '?');
                }
                shown.asText = narrowed.c_str();
                if (!SafeSetValue(row.setting, shown, &code)) {
                    LogFailed("SettingsRegistry::SetValue", code);
                }

                // Enter: the engine has copied the live text into the committed string, which it
                // does at no other time. The stock Network page answers that by rebuilding itself,
                // so this does the same - deferred by a frame rather than done here, because
                // rebuilding tears down the very rows this loop is walking.
                wchar_t committed[kEditTextMax] = {};
                if (SafeReadStringAt(row.settingObject, kEditBoxCommittedTextOffset, committed,
                                     kEditTextMax, &code) &&
                    committed != row.committed) {
                    Log::Loader("FcsePage: \"" + row.setting->name +
                                "\" committed with Enter - refreshing the page");
                    g_rebuildRequested = true;
                }
                continue; // asText points at a local, so it must not fall through to the shared call
            }
            default:
                continue; // a type with no control to read back
            }

            // SetValue drops a no-op change itself, which is the common case here - most rows have
            // not moved - so there is nothing to compare against first.
            if (!SafeSetValue(row.setting, shown, &code)) {
                LogFailed("SettingsRegistry::SetValue", code);
            }
        }
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

    // Whether the row's FCSE_SLOT_nn actually resolved to a widget. Add*Setting binds the value
    // widget into setting+0x44 and guards every one of its item-adds on that field being non-null -
    // so an unresolved slot leaves the value array empty, and everything that later walks it finds
    // nothing. Logged rather than assumed, because a row with no control looks identical to a
    // working one until it is used.
    bool NativeControlBound(void* settingObject, const char* slotParam) {
        DWORD code = 0;
        SettingFields fields{};
        if (!SafeReadSettingFields(settingObject, &fields, &code)) {
            LogFailed("reading the CValueListSetting's fields", code);
            return false;
        }
        char detail[192];
        sprintf_s(detail, "FcsePage: %s -> setting=0x%08X widget=0x%08X values=0x%08X len=%u",
                  slotParam, reinterpret_cast<unsigned>(settingObject), fields.widget, fields.values,
                  fields.valuesLength);
        Log::Loader(detail);

        if (fields.widget == 0 || fields.values == 0 || fields.valuesLength == 0) {
            Log::Loader(std::string("FcsePage: ") + slotParam +
                        " did not bind a value widget - the row has no control, so it is left "
                        "unseeded and unread");
            return false;
        }
        return true;
    }

    // Every native row below passes handler = 0, deliberately. Every settings row on the stock Game
    // tab does the same, because such a row is driven by the CUISettingBase attached to it and by
    // widget events - not by a button handler. Passing FCSE's hand-rolled handler put the engine
    // down a path that fake vtable cannot satisfy and crashed the game on the click, before any FCSE
    // code ran. Changes are picked up by reading the control back instead, in SyncValuesFromControls.
    //
    // Each returns whether a row was added, which is what the caller counts - a row that was added
    // but failed to bind its control still occupies a slot.

    bool AppendCheckboxRow(void* page, SettingsRegistry::Setting* setting, const char* slotParam,
                           size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));

        void* settingObject = nullptr;
        DWORD code = 0;
        if (!SafeAddBoolSetting(page, LabelStorage().back().c_str(), slotParam, YesText(), NoText(),
                                &settingObject, &code)) {
            LogFailed("CSettingsPage::AddBoolSetting", code);
            return false;
        }
        if (settingObject == nullptr || !NativeControlBound(settingObject, slotParam)) {
            return true;
        }

        // Seed the control from the registry, or the row would show whatever the list happens to
        // start on rather than the value in fcse.ini.
        if (!SafeSetSettingValue(settingObject, setting->value.asCheckbox != 0, &code)) {
            LogFailed("CValueListSetting<bool>::SetValue", code);
            return true;
        }
        ShowSlotCell(row, CellKind::Value);
        LiveRows().push_back({setting, settingObject});
        return true;
    }

    bool AppendChoiceRow(void* page, SettingsRegistry::Setting* setting, const char* slotParam,
                           size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));
        const wchar_t* label = LabelStorage().back().c_str();

        // The item labels go into the same permanent storage as the row label. The engine's own
        // caller hands AddBoolSetting two process-lifetime globals, so nothing proves it copies the
        // strings - and keeping them alive costs a deque entry each.
        //
        // The two arrays are locals because those it does demonstrably copy: SetItems appends each
        // value into the setting's own vector as it walks them.
        std::vector<const wchar_t*> itemLabels;
        std::vector<unsigned> itemValues;
        itemLabels.reserve(setting->choices.size());
        itemValues.reserve(setting->choices.size());
        for (size_t i = 0; i < setting->choices.size(); ++i) {
            LabelStorage().push_back(WidenAscii(setting->choices[i]));
            itemLabels.push_back(LabelStorage().back().c_str());
            itemValues.push_back(static_cast<unsigned>(i));
        }

        void* settingObject = nullptr;
        DWORD code = 0;
        if (!SafeAddValueListSetting(page, label, slotParam,
                                     static_cast<unsigned>(itemLabels.size()), itemLabels.data(),
                                     itemValues.data(), &settingObject, &code)) {
            LogFailed("CSettingsPage::AddValueListSetting", code);
            return false;
        }
        if (settingObject == nullptr || !NativeControlBound(settingObject, slotParam)) {
            return true;
        }

        if (!SafeSetSettingValueDword(settingObject, setting->value.asChoice, &code)) {
            LogFailed("CValueListSetting<unsigned>::SetValue", code);
            return true;
        }
        ShowSlotCell(row, CellKind::Value);
        LiveRows().push_back({setting, settingObject});
        return true;
    }

    bool AppendSliderRow(void* page, SettingsRegistry::Setting* setting, const char* slotParam,
                           size_t row) {
        LabelStorage().push_back(L"   " + WidenAscii(setting->name));

        void* settingObject = nullptr;
        DWORD code = 0;
        if (!SafeAddSliderSetting(page, LabelStorage().back().c_str(), slotParam, setting->minValue,
                                  setting->maxValue, &settingObject, &code)) {
            LogFailed("CSettingsPage::AddSliderSetting", code);
            return false;
        }
        if (settingObject == nullptr) {
            return true;
        }

        SliderFields fields{};
        if (!SafeReadSliderFields(settingObject, &fields, &code)) {
            LogFailed("reading the CSliderSetting's fields", code);
            return true;
        }
        char detail[192];
        sprintf_s(detail, "FcsePage: %s -> setting=0x%08X widget=0x%08X element=0x%08X", slotParam,
                  reinterpret_cast<unsigned>(settingObject), fields.widget, fields.element);
        Log::Loader(detail);

        if (fields.widget == 0 || fields.element == 0) {
            Log::Loader(std::string("FcsePage: ") + slotParam +
                        " did not bind a slider widget - the row has no control, so it is left "
                        "unseeded, unread and hidden");
            return true;
        }

        if (!SafeSetSettingValueDword(settingObject, static_cast<uint32_t>(setting->value.asSlider),
                                      &code)) {
            LogFailed("CSliderSetting::SetValue", code);
            return true;
        }
        ShowSlotCell(row, CellKind::Slider);
        LiveRows().push_back({setting, settingObject});
        return true;
    }

    // The escape hatch, off by default: a plain button whose label carries the value, which is what
    // the Mod Configuration Menu shipped with for months. It asks nothing of the engine beyond
    // AddButton, so it is the thing to fall back to if a native control ever misbehaves on a build
    // this was not tested against. Only a Checkbox is clickable here - cycling a Choice or dragging
    // a Slider is what the native controls are for, and a fallback that half-works would be worse
    // than one that plainly shows the value and sends the player to fcse.ini.
    bool AppendPlainRow(void* page, SettingsRegistry::Setting* setting) {
        std::wstring text = L"   " + WidenAscii(setting->name) + L"   ";
        void* handler = nullptr;
        switch (setting->value.type) {
        case FCSE_SettingType_Checkbox:
            text += setting->value.asCheckbox ? kOnSuffix : kOffSuffix;
            handler = ToggleHandler::Create(setting);
            break;
        case FCSE_SettingType_Choice:
            text += L"[" +
                    WidenAscii(setting->value.asChoice < setting->choices.size()
                                   ? setting->choices[setting->value.asChoice]
                                   : std::string("?")) +
                    L"]";
            break;
        case FCSE_SettingType_Slider:
            text += L"[" + std::to_wstring(setting->value.asSlider) + L"]";
            break;
        case FCSE_SettingType_Text:
            text += L"[" + WidenAscii(setting->text) + L"]";
            break;
        }

        LabelStorage().push_back(text);
        DWORD code = 0;
        if (!SafeAddButton(page, LabelStorage().back().c_str(), handler, &code)) {
            LogFailed("AddButton (plain row)", code);
            return false;
        }
        return true;
    }

    void AppendPluginBlock(void* page, const std::string& displayName,
                           const SettingsRegistry::Group* group, size_t* row) {
        AppendCaption(page, L"Plugin: " + WidenAscii(displayName), row);

        if (group == nullptr || group->settings.empty()) {
            AppendCaption(page, L"   (no settings)", row);
            return;
        }

        for (const std::unique_ptr<SettingsRegistry::Setting>& setting : group->settings) {
            if (*row >= kSlotCount) {
                // The layout declares exactly kSlotCount value widgets. Past that the lookup would
                // miss and the row would appear with no control at all, which is worse than an
                // honest message.
                Log::Loader("FcsePage: out of value slots (" + std::to_string(kSlotCount) +
                            "), skipping \"" + setting->name + "\" and anything after it");
                return;
            }

            // Both slot banks are indexed by *row*, not by setting: their widgets are absolutely
            // positioned siblings at the nth row's y coordinate, so a caption row consumes an index
            // exactly like a settings row does. Every row has one cell of each kind authored at its
            // position, because a row's type is not known until a plugin registers; binding one
            // leaves the other unused, and unused is invisible for both.
            char slotParam[24];
            sprintf_s(slotParam, "FCSE_SLOT_%02zu", *row + 1);
            char sliderSlotParam[24];
            sprintf_s(sliderSlotParam, "FCSE_SLIDER_%02zu", *row + 1);

            bool added = false;
            if (g_plainRows) {
                added = AppendPlainRow(page, setting.get());
            } else {
                switch (setting->value.type) {
                case FCSE_SettingType_Checkbox:
                    added = AppendCheckboxRow(page, setting.get(), slotParam, *row);
                    break;
                case FCSE_SettingType_Choice:
                    added = AppendChoiceRow(page, setting.get(), slotParam, *row);
                    break;
                case FCSE_SettingType_Slider:
                    added = AppendSliderRow(page, setting.get(), sliderSlotParam, *row);
                    break;
                case FCSE_SettingType_Text:
                    // No slot cell: a string has no CUISettingBase behind it, so this is a plain
                    // button showing its value, and clicking it opens the game's own text prompt.
                    added = AppendTextRow(page, setting.get(), *row);
                    break;
                default:
                    AppendCaption(page, L"   " + WidenAscii(setting->name) + L" (unsupported type)",
                                  row);
                    continue;
                }
            }

            if (!added) {
                return; // the row list is in an unknown state; appending more would compound it
            }
            ++*row;
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

    // FCSE's three vtable overrides. MSVC will not let a free function be __thiscall, so each is a
    // real member function on a throwaway type - `this` is the engine's page, never an instance of
    // this struct. Reached only through g_pageVtable, which only FCSE's own page points at, so
    // unlike the hook these replaced there is no other instance to tell apart.
    // Each signature has to match the slot it replaces exactly, because __thiscall is
    // callee-cleanup: a thunk declaring the wrong number of stack arguments unbalances the caller's
    // stack rather than merely misbehaving. Display takes none (the stock body ends in a plain
    // RET); Update and OnSettingChanged take one each (both stock bodies end in RET 4).
    struct PageVtableThunk {
        void Display();
        void Update(float deltaTime);
        void OnSettingChanged(void* action);
        void ApplySettings();
        void RefreshSettings();
    };

    // Slot +0x08. The stock body is { RefreshOptionList(); this+0x200 = 0; base::Display(); }; this
    // is the same with our own content build in place of the Game tab's.
    void PageVtableThunk::Display() {
        void* page = reinterpret_cast<void*>(this);
        if (g_pageReady) {
            RebuildRows(page);
        }
        // Else: this is the display Init() triggers, while the page's widgets are still being
        // bound. Chaining straight to the base leaves an empty page for the fraction of a second
        // before the player can reach it, and the next display builds it properly.

        DWORD code = 0;
        SafeWritePointer(page, kDisplayResetFieldOffset, nullptr, &code);
        if (!SafeBaseDisplay(page, &code)) {
            LogFailed("CFCXBaseOptionPage::Display", code);
        }
    }

    // Slot +0x10. The stock body is the base class's per-frame tick followed by a switch on
    // this+0x200 that drives the Game tab's difficulty/machete message-box flow - and one of that
    // switch's three branches reaches the option ids. Forwarding to the base and stopping is what
    // makes that state field inert: several functions in this class write it, but this slot is its
    // only reader, so with the switch gone it does not matter who sets it.
    void PageVtableThunk::Update(float deltaTime) {
        void* page = reinterpret_cast<void*>(this);
        DWORD code = 0;

        // Also where the "unsaved changes" flag is kept clear. Doing it here rather than only in
        // OnSettingChanged means it does not matter whether CFCXBaseOptionPage::SetDirty runs before
        // or after the change notification, or which other path might set it - by the time the
        // player can press Back, a frame has gone by and the flag is false. One byte a frame.
        SafeClearDirtyFlag(page, &code);

        // And where typed text is captured. Every other control announces itself through the
        // OnSettingChanged slot, but a Text row has no CUISettingBase behind it, so the engine has
        // nothing to announce - and a player who types and then backs out never triggers the rebuild
        // that would otherwise read the field. Polling is the only thing that sees those edits.
        //
        // Cheap despite the frequency: SetValue drops a value that has not changed, so a frame where
        // nothing moved costs one read per live row and touches neither the registry nor the file.
        SyncValuesFromControls();

        // Acted on here, one frame after it was asked for, so the rows a requester was walking are
        // long since out of scope.
        if (g_rebuildRequested) {
            g_rebuildRequested = false;
            RebuildRows(page);
        }

        if (!SafeBaseUpdate(page, deltaTime, &code)) {
            LogFailed("CUIPageBase::Update", code);
        }
    }

    // Slot +0x4c, and the one that was crashing the game on every value change.
    //
    // CSettingsPage::OnActionSignal (0x10cdde80) offers each incoming action to every setting on the
    // page; when one consumes it - which is how a row's value actually changes - the base calls this
    // slot through the *primary* vtable, with the action. The stock body forwards to FUN_1081f6c0,
    // which looks up SETTING_DIFFICULTY's button id, gets a setting of the wrong type back, nulls
    // the pointer and dereferences it anyway.
    //
    // So this slot is both the fault and the fix: it is the engine telling us, at exactly the right
    // moment, that a value changed. The setting has already updated itself by the time we are
    // called, so reading the controls back here is all that is needed.
    void PageVtableThunk::OnSettingChanged(void* /*action*/) {
        SyncValuesFromControls();

        // The change is already in fcse.ini by the line above, so the page has nothing pending and
        // must not claim otherwise when the player backs out. Update() clears this too, belt and
        // braces - see the comment there.
        DWORD code = 0;
        SafeClearDirtyFlag(reinterpret_cast<void*>(this), &code);
    }

    // Slots +0x50 and +0x54 - the engine's "apply my settings to the game options" and "reload my
    // settings from the game options". Both are meaningless for a page whose settings are FCSE's,
    // and both walk the same option ids: see the vtable comment at the top of this file.
    //
    // Apply is not merely dropped, it is repurposed. It is the engine telling us a value changed,
    // which is exactly when fcse.ini should be written - so this is also what makes a toggle persist
    // on the click rather than on the next display.
    void PageVtableThunk::ApplySettings() {
        SyncValuesFromControls();
    }

    void PageVtableThunk::RefreshSettings() {
        // Nothing to reload from: FCSE's values live in the registry, and the controls were seeded
        // from it when the rows were built.
    }

    // Templated because the five thunks no longer share one signature. Safe for the same reason the
    // non-templated version was: PageVtableThunk has no bases and no virtuals, so MSVC represents a
    // pointer-to-member-function of it as a single code address.
    template <typename Fn>
    void* RawThunkPointer(Fn fn) {
        union {
            Fn member;
            void* raw;
        } converter;
        converter.member = fn;
        return converter.raw;
    }

    // Point the page at FCSE's own copy of its class vtable, with the three Game-tab-specific slots
    // replaced. Must run before Init(), because Init triggers a display and the stock Display is
    // what would otherwise build - and bind ids to - the Game tab's own rows.
    bool InstallPageVtable(void* page, uintptr_t vtableAddress) {
        DWORD code = 0;
        for (size_t slot = 0; slot < kPageVtableSlots; ++slot) {
            void* value = nullptr;
            if (!SafeReadPointer(reinterpret_cast<void*>(vtableAddress),
                                 static_cast<ptrdiff_t>(slot * sizeof(void*)), &value, &code)) {
                LogFailed("reading CFCXOptionGamePage's vtable", code);
                return false;
            }
            g_pageVtable[slot] = value;
        }

        g_pageVtable[kDisplaySlot] = RawThunkPointer(&PageVtableThunk::Display);
        g_pageVtable[kUpdateSlot] = RawThunkPointer(&PageVtableThunk::Update);
        g_pageVtable[kSettingChangedSlot] = RawThunkPointer(&PageVtableThunk::OnSettingChanged);
        g_pageVtable[kApplySlot] = RawThunkPointer(&PageVtableThunk::ApplySettings);
        g_pageVtable[kRefreshSlot] = RawThunkPointer(&PageVtableThunk::RefreshSettings);

        if (!SafeWritePointer(page, 0, g_pageVtable, &code)) {
            LogFailed("writing the page's vtable pointer", code);
            return false;
        }
        Log::Loader("FcsePage: installed a private " + std::to_string(kPageVtableSlots) +
                    "-slot vtable - Display, Update, OnSettingChanged, apply and refresh are "
                    "FCSE's; the stock Game tab keeps the engine's table");
        return true;
    }

    // Fall back to the row rendering the Mod Configuration Menu shipped with: a plain button whose
    // label carries [ON]/[OFF]. Off by default - native controls are the point of this page - but
    // kept because it asks nothing of the engine and is the thing to reach for if a native control
    // misbehaves on a build this was not tested against.
    bool PlainRows() { return ReadFlag("Plain label rows"); }

}

// Builds the single private page, once per session. `optionsMenuThis` is whichever Options screen
// happened to be shown first; it supplies the initial owning CGameMenu and parent page, both of which
// NavigationHandler::OnActivate rebinds to the screen actually clicked before every switch.
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

    // Before Init, not after: Init triggers a display, and the stock Display is what would build the
    // Game tab's eight rows and bind their button ids into +0x1d8..+0x1fc. Taking the table over
    // first is what keeps those ids at the -1 the constructor left them at, for good.
    if (!InstallPageVtable(page, AddressLibrary::Address(Symbols::kPageVtable))) {
        Log::Loader("FcsePage: could not install the private vtable - the page would run the Game "
                    "tab's own content build and crash on the first click, so it is not offered "
                    "this session");
        return false;
    }

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
    NavigationHandler* handler = NavigationHandler::Create(optionsMenuThis, g_page);
    if (!SafeAddButton(optionsMenuThis, L"Mod Configuration Menu", handler, &code)) {
        LogFailed("AddButton (Options row)", code);
        return false;
    }
    Log::Loader("FcsePage: added the Mod Configuration Menu row to an Options screen");
    return true;
}

bool FcsePage::OwnsPage(void* page) {
    return g_page != nullptr && page == g_page;
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

} // namespace FCSE
