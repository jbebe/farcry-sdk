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
    // table's value. Entries 5 to 10 are the Datsun's range, and placed Datsuns keep an index in it.
    constexpr Paint kPaints[] = {
        {5, 0x344E41}, {6, 0xBAB192}, {7, 0xD3DDE9}, {8, 0xD79736}, {9, 0x871B11}, {10, 0x062C6D},
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
