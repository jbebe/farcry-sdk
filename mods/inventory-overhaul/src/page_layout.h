// The Inventory page as text and colours, and drawing it. Nothing here reaches into the engine, so
// a page can be drawn outside the game too (tools/page_preview.cpp).
#pragma once

#include <cstdint>
#include <string>
#include <vector>
#include <windows.h>

namespace InventoryOverhaul::PageLayout {

constexpr int kSize = 1024;

// A line on the page: its text, a note at the line's right end in its own colour, and whether it is
// marked as the one in hand.
struct Line {
    std::wstring text;
    std::wstring note;
    COLORREF noteColour = 0;
    bool marked = false;
};

struct Model {
    std::vector<Line> hands;
    std::vector<Line> items;
    // The post-it: a gun's name, its condition as the note, and its ammo.
    Line postIt;
    std::wstring postItAmmo;

    // Everything the page shows, so that it is drawn again only when this changes.
    std::wstring Key() const;
};

// A gun's condition as a word in its colour, never a number: the engine counts wear from 1 to 0.
Line Condition(float reliability);

// Draws `model` and returns its pixels, kSize by kSize, 0xAARRGGBB, top row first; they stay valid
// until the next draw. Null when GDI could not make the bitmap.
const uint32_t* Draw(const Model& model);

}
