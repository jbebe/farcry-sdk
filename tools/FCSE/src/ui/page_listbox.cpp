#include "ui/page_internal.h"

#include "log.h"
#include "util/member_fn.h"
#include "util/seh.h"

#include <cstdint>
#include <string>

// Scrolling the settings page, ported from FC2JackalFix's options page. The engine's own refusal to
// move the selection past the last row is the signal to move the window; how that hangs off
// magma::ListBox is in docs/docs/engine-internals/fcse-settings-page-abi.md.
namespace FCSE {
namespace page {

    namespace {

    // FCSE's private copy of magma::ListBox's vtable, preceded by the RTTI pointer the compiler
    // puts one slot before it. Shared by the row list and by the value spinners, which is what lets
    // one thunk tell them apart by `this`.
    void* g_listVtableWithRtti[kListBoxVtableSlots + 1];
    void** const g_listVtable = &g_listVtableWithRtti[1];
    void** g_stockListVtable = nullptr;
    ListBoxNavFn g_stockNav = nullptr;

    void* g_rowList = nullptr;

    // What the last navigation arrived through. SetHighlight needs both and nothing else hands them
    // out.
    void* g_rowListFocusable = nullptr;
    uint32_t g_inputUser = 0;

    bool ScrollWindow(void* page, int direction);

    // One row, moving the window when the step runs off the end of it. Explicit rather than left to
    // the engine, because the input that gets here was routed to a widget the engine would not move
    // the rows for.
    bool StepRow(void* page, int direction, bool allowScroll) {
        if (g_rowList == nullptr) {
            return false;
        }
        DWORD code = 0;
        int32_t selected = -1;
        if (!SehRead(g_rowList, kListBoxSelectedOffset, &selected, &code)) {
            LogFailed("reading the row list's selection", code);
            return false;
        }

        // A selection outside the window is not a step; start just outside the near end.
        int from = selected;
        if (from < 0 || from >= static_cast<int>(Window::kLines)) {
            from = direction > 0 ? -1 : static_cast<int>(Window::kLines);
        }

        int next = from + direction;
        if (next >= 0 && next < static_cast<int>(g_window.Visible())) {
            if (!SafeSetSelection(g_rowList, next, &code)) {
                LogFailed("magma::ListBox::SetSelection", code);
                return false;
            }
            // And light it, which selecting alone does not do.
            if (g_rowListFocusable != nullptr &&
                !SafeSetHighlight(g_rowList, g_rowListFocusable, g_inputUser, next,
                                  kHighlightKeyboard, &code)) {
                LogFailed("magma::ListBox::SetHighlight", code);
            }
            return true;
        }

        return allowScroll && ScrollWindow(page, direction) && StepRow(page, direction, false);
    }

    // Moves the window one row and rebuilds the lines under it, keeping the selection on the row it
    // was on. At an end the window joins to the other, where there is no such row - so the
    // selection is dropped and the caller's next step lands on the fresh one.
    bool ScrollWindow(void* page, int direction) {
        if (page == nullptr || g_rowList == nullptr) {
            return false;
        }

        DWORD code = 0;
        int32_t selected = -1;
        SehRead(g_rowList, kListBoxSelectedOffset, &selected, &code);
        size_t was = selected >= 0 ? g_window.RowOf(static_cast<size_t>(selected)) : Window::kNoRow;

        bool joined = false;
        if (!g_window.Scroll(direction, &joined)) {
            return false;
        }
        RedisplayContent(page);

        if (!SafeSetSelection(g_rowList, joined ? -1 : g_window.LineOf(was), &code)) {
            LogFailed("magma::ListBox::SetSelection", code);
        }
        return true;
    }

    // The navigation slot, for this page's list and its value spinners alone - the table is swapped
    // per instance, so nothing else reaches it. MSVC will not let a free function be __thiscall, so
    // it is a member of a throwaway type: `this` is the engine's ListBox, never an instance of this
    // struct. Same shape as the page overrides in page_vtable.cpp.
    struct ListBoxThunk {
        int Nav(void* sender, void* event, uint8_t* result);
    };

    int ListBoxThunk::Nav(void* sender, void* event, uint8_t* result) {
        void* listBox = reinterpret_cast<void*>(this);
        DWORD code = 0;

        if (listBox == g_rowList && sender != nullptr) {
            g_rowListFocusable = sender;
        }
        uint8_t user = 0;
        if (SehRead(event, kNavEventUserOffset, &user, &code)) {
            g_inputUser = user;
        }

        uint8_t flagsBefore = 0;
        SehRead(result, kNavResultFlagsOffset, &flagsBefore, &code);

        int returned = 0;
        if (!SehCallRet(&code, &returned, g_stockNav, listBox, sender, event, result)) {
            LogFailed("magma::ListBox navigation", code);
            return 0;
        }
        if (result == nullptr || g_page == nullptr) {
            return returned;
        }

        // The engine raising its unhandled flag on this call is the refusal; a flag something
        // upstream had already set is not.
        uint8_t flagsAfter = 0;
        int32_t navCode = -1;
        if (!SehRead(result, kNavResultFlagsOffset, &flagsAfter, &code) ||
            !SehRead(result, kNavResultCodeOffset, &navCode, &code)) {
            LogFailed("reading the navigation result", code);
            return returned;
        }
        bool refused = (flagsAfter & kNavFlagUnhandled) != 0 && (flagsBefore & kNavFlagUnhandled) == 0;
        if (!refused || (navCode != kNavCodeUp && navCode != kNavCodeDown)) {
            return returned;
        }
        int direction = navCode == kNavCodeDown ? 1 : -1;

        // A focused value spinner only knows left and right, so up and down reach it and are
        // refused. Move the rows by hand and give the light back to them.
        if (listBox != g_rowList) {
            SafeSetHighlight(listBox, sender, g_inputUser, -1, kHighlightPointer, &code);
            SafeSetHighlight(listBox, sender, g_inputUser, -1, kHighlightKeyboard, &code);
            StepRow(g_page, direction, true);
            SehWriteByte(result, kNavResultFlagsOffset, flagsBefore, &code);
            return 1;
        }

        if (!ScrollWindow(g_page, direction)) {
            return returned;
        }

        // The stock step onto the fresh row, for its click sound. After a join there is no selection
        // to step from, and only wrap reaches the far row from there.
        int32_t selected = -1;
        uint8_t flags = 0;
        SehRead(listBox, kListBoxSelectedOffset, &selected, &code);
        if (SehRead(listBox, kListBoxFlagsOffset, &flags, &code) && selected < 0) {
            SehWriteByte(listBox, kListBoxFlagsOffset, flags | kListBoxWrapFlag, &code);
        }
        SehWriteByte(result, kNavResultFlagsOffset, flagsBefore, &code);
        SehCallRet(&code, &returned, g_stockNav, listBox, sender, event, result);
        SehWriteByte(listBox, kListBoxFlagsOffset, flags & ~kListBoxWrapFlag, &code);

        // Swallow the refusal, or the key also reaches whatever sits beyond the list.
        SehWriteByte(result, kNavResultFlagsOffset, flagsBefore, &code);
        return returned;
    }

    // Every list shares one class vtable, so the table is swapped per instance rather than hooked.
    bool InstallListVtable(void* listBox, void** stockVtable) {
        // The RTTI pointer sits one slot before the table and has to travel with the copy.
        if (!CopyVtable(stockVtable - 1, g_listVtableWithRtti, kListBoxVtableSlots + 1,
                        "magma::ListBox")) {
            return false;
        }
        g_stockNav = reinterpret_cast<ListBoxNavFn>(g_listVtable[kListBoxNavSlot]);
        g_listVtable[kListBoxNavSlot] = RawFunctionPointer(&ListBoxThunk::Nav);

        DWORD code = 0;
        if (!SehWritePointer(listBox, 0, g_listVtable, &code)) {
            LogFailed("writing the row list's vtable pointer", code);
            return false;
        }
        g_stockListVtable = stockVtable;
        return true;
    }

    // The value spinners get the same table; it is the only way a focused one passes up and down
    // back to the rows. Only those still carrying the stock table, so a cell holding another class
    // is left alone.
    void AdoptValueSpinners() {
        for (size_t line = 0; line < Window::kLines; ++line) {
            void* widget = SlotCellWidget(line, CellKind::Value);
            void* widgetVtable = nullptr;
            DWORD code = 0;
            if (widget != nullptr && SehReadPointer(widget, 0, &widgetVtable, &code) &&
                widgetVtable == g_stockListVtable) {
                SehWritePointer(widget, 0, g_listVtable, &code);
            }
        }
    }

    }

    void DisableRowItem(void* page, size_t line) {
        DWORD code = 0;
        void* listBox = nullptr;
        void* items = nullptr;
        void* item = nullptr;
        if (!SehReadPointer(page, kRowListBoxOffset, &listBox, &code) || listBox == nullptr ||
            !SehReadPointer(listBox, kListBoxItemsOffset, &items, &code) || items == nullptr ||
            !SehReadPointer(items, static_cast<ptrdiff_t>(line * sizeof(void*)), &item, &code) ||
            item == nullptr) {
            Log::Loader("FcsePage: no list item for line " + std::to_string(line + 1) +
                        " - leaving the row enabled");
            return;
        }
        if (!SehWriteByte(item, kListItemDisabledOffset, 1, &code)) {
            LogFailed("disabling a row's list item", code);
        }
    }

    void TakeOverRowList(void* page) {
        if (!ScrollingAvailable()) {
            return; // logged once when the page was built
        }

        DWORD code = 0;
        void* listBox = nullptr;
        if (!SehReadPointer(page, kRowListBoxOffset, &listBox, &code) || listBox == nullptr) {
            return;
        }
        void* vtable = nullptr;
        if (!SehReadPointer(listBox, 0, &vtable, &code)) {
            LogFailed("reading the row list's vtable pointer", code);
            return;
        }

        if (vtable != g_listVtable) {
            if (!InstallListVtable(listBox, static_cast<void**>(vtable))) {
                return;
            }
            Log::Loader("FcsePage: took the row list's navigation over - the page scrolls past its "
                        + std::to_string(Window::kLines) + " lines");
        }
        g_rowList = listBox;
        AdoptValueSpinners();

        // The viewport is the layout's line count, and wrap is off so a press past the last row is
        // reported rather than silently taken back to the first.
        SehWriteByte(listBox, kListBoxMaxVisibleOffset, static_cast<unsigned char>(Window::kLines),
                     &code);
        uint8_t flags = 0;
        if (SehRead(listBox, kListBoxFlagsOffset, &flags, &code)) {
            SehWriteByte(listBox, kListBoxFlagsOffset, flags & ~kListBoxWrapFlag, &code);
        }
    }

}
}
