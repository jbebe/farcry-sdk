#include "paint.h"

#include "engine/paint_table.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    struct Paint {
        unsigned index;
        uint32_t rgb;
    };

    // Chosen by the user (2026-10-19) as the colour the body shows; the car's grey detail texture halves the
    // table's value. Each car keeps its own range, which cars placed in the world keep an index in; the
    // Liberty's, the Wrangler's and the rest of the buggy's are the game's.
    constexpr Paint kPaints[] = {
        // The buggy's.
        {2, 0xD3DDE9},
        // The Datsun's, slot 6 also the taxi's when it rolls none of the Wrangler's.
        {5, 0x344E41}, {6, 0xBAB192}, {7, 0xD3DDE9}, {8, 0xD79736}, {9, 0x871B11}, {10, 0x062C6D},
        // The Land Rover's.
        {14, 0x80958C}, {15, 0x3D421A}, {16, 0xEA5A17}, {17, 0x1C5672}, {18, 0xCDBCA8}, {19, 0x547E8C},
        {20, 0xCDBCA8}, {21, 0xFCBA4A},
        // The big truck's.
        {47, 0x3C7C9B}, {48, 0x1B517F}, {49, 0x27775C}, {50, 0xA6A394}, {51, 0x3C7C9B},
    };

    float Channel(uint32_t rgb, int shift) { return static_cast<float>((rgb >> shift) & 0xFF) / 255.0f * 2.0f; }
}

namespace VehicleOverhaul::Paint {

bool Install() {
    for (const auto& [index, rgb] : kPaints) {
        PaintTable::Entry* entry = PaintTable::At(index);
        if (entry == nullptr) {
            FCSE::Logf("paint: the vehicle paint table was not found; the game's paints stay");
            return false;
        }
        entry->paint[0] = Channel(rgb, 16);
        entry->paint[1] = Channel(rgb, 8);
        entry->paint[2] = Channel(rgb, 0);
    }
    return true;
}

}
