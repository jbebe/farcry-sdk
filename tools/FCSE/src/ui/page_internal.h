#pragma once

#include "api/settings_registry.h"
#include "ui/engine_page_abi.h"
#include "ui/page_window.h"

#include <deque>
#include <string>
#include <vector>
#include <windows.h>

// Shared between the translation units that build FCSE's settings page: fcse_page.cpp owns the
// page's lifetime and state, page_rows.cpp plans and builds its rows, page_slots.cpp resolves and
// binds the layout's cells, page_vtable.cpp carries the class-vtable overrides, and
// page_listbox.cpp takes the row list's navigation over to scroll the window.
//
// Internal to ui/ - ui/fcse_page.h is what the rest of the loader uses.
namespace FCSE {
namespace page {

// The engine entry points FcsePage::Install resolves are private to fcse_page.cpp, reached from
// here only through the Safe* wrappers below - which is what keeps every call into the engine
// behind an SEH guard. These few are the exception, because the code that needs them lives
// elsewhere: the vtable overrides chain to the base implementations, and a forged std::string has
// to carry the proxy pointer the engine's own strings do.
extern DisplayFn g_baseOptionPageDisplay;
extern UpdateFn g_baseUpdate;
extern const void* g_emptyStringProxy;

extern void* g_page;
extern bool g_plainRows;
extern void* g_pageVtable[kPageVtableSlots];
extern bool g_pageReady;
extern bool g_rebuildRequested;

// Which slice of the plan is on screen. Reset to the top on every visit to the page.
extern Window g_window;

void LogFailed(const char* what, DWORD code);

// Owns every label ever handed to the engine, because AddButton is only known to store the
// pointer rather than copy the text.
std::deque<std::wstring>& LabelStorage();

// Puts a label in that storage and returns the address the engine may keep.
const wchar_t* StoreLabel(const std::wstring& text);

std::wstring WidenAscii(const std::string& text);

// The engine's cached localised strings when the player has been to the stock Game tab this
// session, English otherwise. Never null, so the caller has nothing to check.
const wchar_t* YesText();
const wchar_t* NoText();

bool SafeAddButton(void* thisPtr, const wchar_t* label, void* handler, DWORD* outCode);
bool SafeAddBoolSetting(void* page, const wchar_t* label, const char* slotParam,
                        const wchar_t* yesText, const wchar_t* noText, bool enabled,
                        void** outSetting, DWORD* outCode);
bool SafeAddValueListSetting(void* page, const wchar_t* label, const char* slotParam,
                             unsigned count, const wchar_t* const* itemLabels,
                             const unsigned* itemValues, bool enabled, void** outSetting,
                             DWORD* outCode);
bool SafeAddSliderSetting(void* page, const wchar_t* label, const char* slotParam, int minValue,
                          int maxValue, bool enabled, void** outSetting, DWORD* outCode);
bool SafeSetElementVisible(void* element, bool visible, DWORD* outCode);
bool SafeGetUserDataElement(void* userData, const NarrowString* name, void** outElement,
                            DWORD* outCode);
bool SafeEditBoxSetText(void* editBox, const WideString* text, DWORD* outCode);
bool SafeSetSelected(void* magmaPage, void* focusable, DWORD* outCode);
bool SafeSetSelection(void* listBox, int index, DWORD* outCode);
bool SafeSetHighlight(void* listBox, void* focusable, uint32_t user, int index, int channel,
                      DWORD* outCode);

// False on a build whose mapping is missing the two magma::ListBox calls a scroll needs, which
// leaves the page showing its first Window::kLines rows and nothing else.
bool ScrollingAvailable();

// The fields AddBoolSetting initialises on the CValueListSetting it creates:
//   +0x44 the bound value widget, +0x4c the value array, +0x50 its length in bytes.
struct SettingFields {
    unsigned widget;
    unsigned values;
    unsigned valuesLength;
};

bool SafeReadSettingFields(void* settingObject, SettingFields* out, DWORD* outCode);

// The fields CSliderSetting::FetchMagmaElements writes, which are NOT the ones the value-list
// variant uses. A slider has no value array either, so the widget being non-null is the whole test
// that the slot resolved.
struct SliderFields {
    unsigned widget;  // +0x48
    unsigned element; // +0x4c
};

bool SafeReadSliderFields(void* settingObject, SliderFields* out, DWORD* outCode);

// Which of the three banks a row's control lives in.
enum class CellKind { Value, Slider, Edit };

// The name a cell is authored under in fcse.mgb, for `row` counted from 0. The cache resolves
// cells under these names and the row builders bind against them, so the two must agree - and a
// disagreement is silent, leaving the row bound to nothing.
constexpr size_t kSlotParamMax = 24;
void SlotParamName(size_t row, CellKind kind, char (&out)[kSlotParamMax]);

// The rows built by the previous display, so their controls can be read back before the list is
// cleared. Rebuilt from scratch every display.
struct LiveRow {
    SettingsRegistry::Setting* setting;
    void* settingObject;

    // Text rows only: the EditBox's committed string as it was when the row was built. The engine
    // copies the live text into it when the player presses Enter and at no other time, so a change
    // here is precisely an Enter - which is what the stock Network page treats as "commit and
    // refresh the page".
    std::wstring committed;
};

std::vector<LiveRow>& LiveRows();

// An EditBox's two strings, and the longest FCSE reads out of either.
constexpr ptrdiff_t kEditBoxDisplayedTextOffset = 0x8c;
constexpr ptrdiff_t kEditBoxCommittedTextOffset = 0xa8;
constexpr size_t kEditTextMax = 64;

bool SafeReadStringAt(void* widget, ptrdiff_t offset, wchar_t* out, size_t max, DWORD* outCode);
bool SafeReadEditText(void* widget, wchar_t* out, DWORD* outCode);

void CacheSlotCells(void* page);
void HideAllSlotCells();
void* SlotCellElement(size_t row, CellKind kind);
void* SlotCellWidget(size_t row, CellKind kind);
void ShowSlotCell(size_t row, CellKind kind);
void BindEditCell(SettingsRegistry::Setting* setting, size_t row);

void SyncValuesFromControls();

// Rebuilds the page's contents. Deferred to the next Update when requested from inside a walk of
// the rows a rebuild destroys.
void RebuildRows(void* page);

// The stock Display's own two steps: clear the state field the Game tab's Update switch reads, then
// run the base implementation.
void BaseDisplay(void* page);

// A rebuild the player sees without a fresh display - what a scroll does to put the new window up.
void RedisplayContent(void* page);

// Points this page's row list at FCSE's own copy of magma::ListBox's vtable, so a press past the
// last row scrolls instead of stopping. A no-op once done, and on a build where scrolling is off.
void TakeOverRowList(void* page);

// Marks a built row disabled: greyed, and refused by SetSelection so the player cannot land on it.
// For rows added through AddButton, which - unlike Add*Setting - has no enabled parameter.
void DisableRowItem(void* page, size_t line);

// Copies `slots` pointers from an engine class vtable into `out`, which the caller then overwrites
// the slots it owns in. `what` names the table in the log if a read faults.
bool CopyVtable(const void* source, void** out, size_t slots, const char* what);

bool InstallPageVtable(void* page, uintptr_t vtableAddress);

}
}
