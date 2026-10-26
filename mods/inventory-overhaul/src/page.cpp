// The page is drawn with GDI into a bitmap, in Windows' own handwriting face, and handed to the
// paper. It is drawn again only when the text on it changes.
#include "page.h"

#include "engine/economy.h"
#include "engine/inventory.h"
#include "engine/paper.h"
#include "items.h"
#include "two_guns.h"

#include <algorithm>
#include <string>
#include <vector>
#include <windows.h>

namespace InventoryOverhaul::Page {

namespace {
    constexpr int kSize = 1024;
    constexpr const wchar_t* kFace = L"Ink Free";

    constexpr COLORREF kPaper = RGB(232, 222, 196);
    constexpr COLORREF kRule = RGB(180, 196, 210);
    constexpr COLORREF kFold = RGB(200, 188, 160);
    constexpr COLORREF kInk = RGB(38, 46, 82);
    constexpr COLORREF kPostIt = RGB(244, 226, 120);
    constexpr COLORREF kPostItShadow = RGB(196, 182, 150);

    // The two pages, side by side, and the ruled lines' spacing.
    constexpr int kLeft = 56;
    constexpr int kRight = 552;
    constexpr int kPageWidth = 424;
    constexpr int kTop = 48;
    constexpr int kRow = 48;

    struct Line {
        std::wstring text;
        std::wstring note;
        COLORREF noteColour = kInk;
        bool marked = false;
    };

    struct Model {
        std::vector<Line> hands;
        std::vector<Line> items;
        Line postIt;
        std::wstring postItAmmo;

        std::wstring Key() const {
            std::wstring key;
            for (const std::vector<Line>* list : {&hands, &items}) {
                for (const Line& line : *list) {
                    key += line.text + L'|' + line.note + (line.marked ? L"*" : L"") + L'\n';
                }
            }
            return key + postIt.text + L'|' + postIt.note + L'|' + postItAmmo;
        }
    };

    struct Canvas {
        HDC dc = nullptr;
        uint32_t* pixels = nullptr;
    };

    std::wstring g_shown;

    // "weapons.Primary.M249_Saw" is the M249 Saw.
    std::wstring DisplayName(const std::string& archetype) {
        std::string name = archetype.substr(archetype.find_last_of('.') + 1);
        std::replace(name.begin(), name.end(), '_', ' ');
        return std::wstring(name.begin(), name.end());
    }

    // A gun's condition as a word, never a number: the engine counts wear from 1 down to 0.
    Line Condition(float reliability) {
        if (reliability >= 0.75f) {
            return {L"", L"new", RGB(46, 125, 50)};
        }
        if (reliability >= 0.5f) {
            return {L"", L"used", RGB(124, 112, 20)};
        }
        if (reliability >= 0.25f) {
            return {L"", L"worn", RGB(196, 96, 0)};
        }
        return {L"", L"failing", RGB(183, 28, 28)};
    }

    Model Build(const void* view, void* inventory) {
        Model model;
        std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        std::sort(guns.begin(), guns.end(), [](const Inventory::Weapon& a, const Inventory::Weapon& b) {
            return TwoGuns::SlotOf(a.weapon) < TwoGuns::SlotOf(b.weapon);
        });
        const Inventory::Weapon* shown = nullptr;
        for (const Inventory::Weapon& gun : guns) {
            const bool inHand = Inventory::Id(gun.item) == Inventory::InHandId(inventory);
            Line line = Condition(Inventory::Reliability(gun.weapon));
            line.text = L"(" + std::to_wstring(TwoGuns::SlotOf(gun.weapon)) + L")  " +
                        DisplayName(Inventory::Name(gun.weapon));
            line.marked = inHand;
            model.hands.push_back(line);
            if (shown == nullptr || inHand) {
                shown = &gun;
            }
        }
        for (const Items::Record& item : Items::Load()) {
            model.items.push_back({item.gun ? DisplayName(item.id)
                                            : std::wstring(item.id.begin(), item.id.end()) +
                                                  (item.count > 1 ? L"  \u00d7" +
                                                                        std::to_wstring(item.count)
                                                                  : L"")});
        }
        const int diamonds = Economy::Diamonds();
        if (diamonds > 0) {
            model.items.push_back({std::to_wstring(diamonds) + L" diamonds"});
        }
        if (shown != nullptr) {
            model.postIt = Condition(Inventory::Reliability(shown->weapon));
            model.postIt.text = DisplayName(Inventory::Name(shown->weapon));
            model.postItAmmo = L"Ammo " + std::to_wstring(Inventory::Loaded(inventory, shown->weapon)) +
                               L" / " + std::to_wstring(Inventory::SpareRounds(view, shown->weapon));
        }
        return model;
    }

    Canvas& TheCanvas() {
        static Canvas canvas = [] {
            Canvas made;
            BITMAPINFO info{};
            info.bmiHeader.biSize = sizeof(info.bmiHeader);
            info.bmiHeader.biWidth = kSize;
            info.bmiHeader.biHeight = -kSize;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = BI_RGB;
            void* bits = nullptr;
            HBITMAP bitmap = CreateDIBSection(nullptr, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
            if (bitmap == nullptr) {
                return made;
            }
            made.dc = CreateCompatibleDC(nullptr);
            SelectObject(made.dc, bitmap);
            SetBkMode(made.dc, TRANSPARENT);
            made.pixels = static_cast<uint32_t*>(bits);
            return made;
        }();
        return canvas;
    }

    HFONT Font(int height, int weight) {
        return CreateFontW(-height, 0, 0, 0, weight, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                           OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
                           DEFAULT_PITCH, kFace);
    }

    void Fill(HDC dc, int left, int top, int right, int bottom, COLORREF colour) {
        const RECT rect{left, top, right, bottom};
        HBRUSH brush = CreateSolidBrush(colour);
        FillRect(dc, &rect, brush);
        DeleteObject(brush);
    }

    void Text(HDC dc, HFONT font, COLORREF colour, int x, int y, const std::wstring& text,
              UINT align = TA_LEFT) {
        SelectObject(dc, font);
        SetTextColor(dc, colour);
        SetTextAlign(dc, align | TA_BOTTOM);
        TextOutW(dc, x, y, text.c_str(), static_cast<int>(text.size()));
    }

    // A heading and its lines, one ruled line each; returns the ruled line after the last.
    int List(HDC dc, HFONT heading, HFONT body, int x, int y, const wchar_t* title,
             const std::vector<Line>& lines) {
        Text(dc, heading, kInk, x, y, title);
        for (const Line& line : lines) {
            y += kRow;
            if (line.marked) {
                Text(dc, body, kInk, x - 28, y, L"\u25b8");
            }
            Text(dc, body, kInk, x, y, line.text);
            if (!line.note.empty()) {
                Text(dc, body, line.noteColour, x + kPageWidth, y, line.note, TA_RIGHT);
            }
        }
        return y + kRow;
    }

    void Draw(const Canvas& canvas, const Model& model) {
        HDC dc = canvas.dc;
        Fill(dc, 0, 0, kSize, kSize, kPaper);
        for (int y = kTop + kRow; y < kSize - kRow / 2; y += kRow) {
            Fill(dc, 24, y + 4, kSize - 24, y + 6, kRule);
        }
        Fill(dc, kSize / 2 - 3, 0, kSize / 2 + 3, kSize, kFold);

        HFONT heading = Font(46, FW_BOLD);
        HFONT body = Font(38, FW_NORMAL);
        HFONT big = Font(56, FW_BOLD);

        int y = List(dc, heading, body, kLeft, kTop + kRow, L"Hands", model.hands);
        List(dc, heading, body, kLeft, y + kRow, L"Items", model.items);

        if (!model.postIt.text.empty()) {
            const int left = kRight + 8;
            const int top = kTop + kRow * 9;
            const int right = left + kPageWidth - 16;
            const int bottom = top + kRow * 8;
            Fill(dc, left + 10, top + 12, right + 10, bottom + 12, kPostItShadow);
            Fill(dc, left, top, right, bottom, kPostIt);
            Text(dc, big, kInk, left + 28, top + kRow * 2, model.postIt.text);
            Text(dc, body, model.postIt.noteColour, left + 28, top + kRow * 4, model.postIt.note);
            Text(dc, body, kInk, left + 28, top + kRow * 6, model.postItAmmo);
        }

        DeleteObject(heading);
        DeleteObject(body);
        DeleteObject(big);
        GdiFlush();
        // GDI leaves alpha at nought; the paper is opaque.
        for (int i = 0; i < kSize * kSize; ++i) {
            canvas.pixels[i] |= 0xFF000000;
        }
    }
}

void Update(const void* view, void* inventory) {
    const Model model = Build(view, inventory);
    std::wstring key = model.Key();
    if (key == g_shown) {
        return;
    }
    const Canvas& canvas = TheCanvas();
    if (canvas.dc == nullptr) {
        return;
    }
    g_shown = std::move(key);
    Draw(canvas, model);
    Paper::Show(canvas.pixels, kSize);
}

}
