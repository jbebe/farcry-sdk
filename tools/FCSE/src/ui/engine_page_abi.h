#pragma once

#include <cstdint>

// The engine ABI FCSE's settings page is built on: CFCXOptionGamePage's layout, the calls that
// build a row, and the string shapes this MSVC build uses.
//
// Every number here was confirmed by decompile and exercised in a running game. What each one is
// and how it was established lives in docs/docs/engine-internals/fcse-settings-page-abi.md - this
// header is the list, not the reasoning. Addresses are never baked in: the loader resolves them
// through the address library by the Symbols::k* ids named in ui/fcse_page.cpp.
namespace FCSE {

// Allocation size AddPage<CFCXOptionGamePage> uses.
constexpr size_t kPageSize = 0x210;

// CFCXOptionGamePage's primary vtable, of which FCSE keeps a private copy. 26 is exact - the next
// pointer along is a string the constructor pushes, not a 27th entry.
constexpr size_t kPageVtableSlots = 26;
constexpr size_t kDisplaySlot = 2;         // +0x08
constexpr size_t kUpdateSlot = 4;          // +0x10
constexpr size_t kSettingChangedSlot = 19; // +0x4c
constexpr size_t kApplySlot = 20;          // +0x50
constexpr size_t kRefreshSlot = 21;        // +0x54

// Cleared by the stock Display on every display; FCSE's mirrors that.
constexpr ptrdiff_t kDisplayResetFieldOffset = 0x200;

// CSettingsPage::ClearSettings - drop the page's rows and delete their settings.
constexpr ptrdiff_t kClearRowsVtableOffset = 0x40;

// CFCXBaseOptionPage's unapplied-changes flag. FCSE clears it rather than answering the prompt it
// drives: this page writes fcse.ini on the change itself, so nothing is ever pending.
constexpr ptrdiff_t kDirtyFlagOffset = 0x1b8;

// The "any controller" value magma::Page::SetSelected takes, as the layout's DEFAULT_ELEMENT uses.
constexpr int kAnyController = 255;

// Read defensively: the engine only fills its localised YES/NO once the stock Game tab has been
// opened, which FCSE's page no longer does.
constexpr wchar_t kYesFallback[] = L"YES";
constexpr wchar_t kNoFallback[] = L"NO";

// UserData properties, matching what fcse.mgb declares - see tools/FCSE/assets/README.md.
constexpr char kLabelListParam[] = "SETTING_LABEL_LIST";
constexpr size_t kSlotCount = 20;

// CValueListSetting's value accessors, both of which take and return a pointer to the value.
constexpr size_t kSettingSetValueSlot = 13; // vtable +0x34
constexpr size_t kSettingGetValueSlot = 14; // vtable +0x38

// CUIPageBase's page-name std::string: the three fields Init itself reads, so overwriting only
// these leaves the constructor's allocator in place.
constexpr ptrdiff_t kPageNameDataOffset = 0x2c;
constexpr ptrdiff_t kPageNameSizeOffset = 0x3c;
constexpr ptrdiff_t kPageNameCapacityOffset = 0x40;
constexpr size_t kNarrowSsoCapacity = 15;

// CMenuPage's stored title std::wstring.
constexpr ptrdiff_t kTitleDataOffset = 0xf4;
constexpr ptrdiff_t kTitleSizeOffset = 0x104;
constexpr ptrdiff_t kTitleCapacityOffset = 0x108;
constexpr size_t kWideSsoCapacity = 7;

// The area name authored into fcse.mgb and registered in its GenericObjectTable. 9 characters, so
// it lives inline in the page's own SSO buffer.
constexpr char kPageName[] = "FCSE_PAGE";
constexpr wchar_t kTitle[] = L"Mod Configuration";

constexpr ptrdiff_t kBoundMagmaPageOffset = 0x14; // written by SetPage
constexpr ptrdiff_t kRowListElementOffset = 0x08; // written by FetchMagmaElements
constexpr ptrdiff_t kRowListBoxOffset = 0x0c;     //   "
constexpr ptrdiff_t kTitleTextOffset = 0x10;      //   "
constexpr ptrdiff_t kInitedFlagOffset = 0x68;     // set to 1 at the end of Init
constexpr ptrdiff_t kParentPageOffset = 0xec;     // what AddPage<T> stores for its caller

// The owning CGameMenu*, read by CSetNextPageMenuHandler::SwitchPage itself.
constexpr ptrdiff_t kOwnerPageToGameMenuOffset = 0x140;

// CGameMenu's "next page" field. SwitchPage activates it and deactivates +0x40, never touching the
// page hashtable - which is what makes this route avoid the InsertNode crash.
constexpr ptrdiff_t kGameMenuNextPageOffset = 0x3c;

// MSVC's std::wstring as this build lays it out: proxy pointer, 8 inline wchar_t while capacity is
// under 8, size, capacity.
struct WideString {
    const void* proxy;
    wchar_t buffer[8];
    uint32_t size;
    uint32_t capacity;
};

// MSVC's std::string, same shape with a 16-byte inline buffer used while capacity is under 0x10.
struct NarrowString {
    const void* proxy;
    char buffer[16];
    uint32_t size;
    uint32_t capacity;
};

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
using EditBoxSetTextFn = void(__thiscall*)(void* editBox, const WideString* text, char commit);
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
                                              const char* labelListParam, const char* settingParam,
                                              int minValue, int maxValue, int enabled,
                                              void* handler);

}
