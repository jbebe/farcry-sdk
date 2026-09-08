#pragma once

#include "ui/engine_page_abi.h"

#include <cstddef>

namespace FCSE {
namespace page {

// The screen as a window over the page's plan of rows: it moves by rebuilding the layout's lines
// from a different offset into the plan.
struct Window {
    static constexpr size_t kLines = kUsableLineCount;
    static constexpr size_t kNoRow = static_cast<size_t>(-1);

    size_t total = 0;
    size_t top = 0;

    bool Scrolls() const { return total > kLines; }

    // Pulls `top` back into range, which is what a plan that shrank between displays needs.
    // Hand-rolled rather than std::min because windows.h's min macro reaches this header.
    void Clamp() {
        size_t last = Scrolls() ? total - kLines : 0;
        top = top < last ? top : last;
    }

    // How many of the layout's lines this window actually fills.
    size_t Visible() const {
        size_t remaining = top < total ? total - top : 0;
        return remaining < kLines ? remaining : kLines;
    }

    // Moves one row in `direction`. At an edge the window joins to the far end instead, which
    // `outJoined` reports because the caller then has no row to keep the selection on. False when
    // everything fits on screen.
    bool Scroll(int direction, bool* outJoined) {
        *outJoined = false;
        if (!Scrolls()) {
            return false;
        }
        size_t last = total - kLines;
        *outJoined = direction > 0 ? top >= last : top == 0;
        top = *outJoined ? (direction > 0 ? 0 : last) : (direction > 0 ? top + 1 : top - 1);
        return true;
    }

    // The line a plan row is drawn on, or -1 when it is off screen.
    int LineOf(size_t row) const {
        return row >= top && row - top < Visible() ? static_cast<int>(row - top) : -1;
    }

    // The plan row a line draws, or kNoRow past the end of the window.
    size_t RowOf(size_t line) const { return line < Visible() ? top + line : kNoRow; }
};

}
}
