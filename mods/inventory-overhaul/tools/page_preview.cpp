// Draws a sample Inventory page to a .bmp, to look at the page without the game:
//     PagePreview.exe page.bmp
#include "page_layout.h"

#include <cstdio>

using InventoryOverhaul::PageLayout::Condition;
using InventoryOverhaul::PageLayout::Line;
using InventoryOverhaul::PageLayout::Model;
using InventoryOverhaul::PageLayout::kSize;

namespace {
    Line Gun(const wchar_t* text, float reliability, bool marked) {
        Line line = Condition(reliability);
        line.text = text;
        line.marked = marked;
        return line;
    }
}

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "usage: PagePreview <page.bmp>\n");
        return 2;
    }
    Model model;
    model.hands = {Gun(L"(1)  Makarov", 0.92f, false), Gun(L"(2)  AK47", 0.58f, true)};
    model.items = {{L"Carl Gustav"}, {L"M249 Saw"}, {L"courier pouch"}, {L"37 diamonds"}};
    model.postIt = Condition(0.58f);
    model.postIt.text = L"AK47";
    model.postItAmmo = L"Ammo 30 / 120";
    const uint32_t* pixels = InventoryOverhaul::PageLayout::Draw(model);
    if (pixels == nullptr) {
        std::fprintf(stderr, "GDI could not make the bitmap\n");
        return 1;
    }

    BITMAPINFOHEADER header{};
    header.biSize = sizeof(header);
    header.biWidth = kSize;
    header.biHeight = -kSize;
    header.biPlanes = 1;
    header.biBitCount = 32;
    header.biCompression = BI_RGB;
    const DWORD imageBytes = kSize * kSize * 4;
    BITMAPFILEHEADER file{};
    file.bfType = 0x4D42;
    file.bfOffBits = sizeof(file) + sizeof(header);
    file.bfSize = file.bfOffBits + imageBytes;
    std::FILE* out = std::fopen(argv[1], "wb");
    if (out == nullptr) {
        std::fprintf(stderr, "cannot write %s\n", argv[1]);
        return 1;
    }
    std::fwrite(&file, sizeof(file), 1, out);
    std::fwrite(&header, sizeof(header), 1, out);
    std::fwrite(pixels, imageBytes, 1, out);
    std::fclose(out);
    return 0;
}
