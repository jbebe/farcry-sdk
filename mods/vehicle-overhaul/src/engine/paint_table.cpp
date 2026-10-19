#include "engine/paint_table.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    // CVehicleMaterialComponent::ApplyChanges indexing the table by the colour index: LEA, SHL 4, ADD table.
    FCSE::Relocation<uint8_t*> g_lookup{FCSE::Pattern(
        "38 5F 38 0F 85 ?? ?? ?? ?? 8B 47 34 8B 4E 48 8D 04 80 C1 E0 04 05 ?? ?? ?? ?? 50 68")};
    constexpr ptrdiff_t kTable = 0x16;
    constexpr unsigned kEntries = 61;
}

namespace VehicleOverhaul::PaintTable {

Entry* At(unsigned index) {
    if (index >= kEntries || !g_lookup) {
        return nullptr;
    }
    return *reinterpret_cast<Entry**>(g_lookup.get() + kTable) + index;
}

}
