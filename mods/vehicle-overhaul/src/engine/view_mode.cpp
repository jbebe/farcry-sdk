// The player's body as the engine draws it. The player's graphic holds two models, the headless avatar and the
// full body, and CPawn::ViewModeChanged shows one or the other along with the body's shadow and the weapons it
// carries. The engine only ever goes one way: putting first person up discards the full body, so that call is
// taken out and the switch works both ways.
#include "engine/view_mode.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using ViewModeChangedFn = void(__thiscall*)(void* pawn, bool firstPerson);

    FCSE::Relocation<ViewModeChangedFn> g_viewModeChanged{FCSE::Pattern(
        "53 8B 5C 24 08 55 56 57 53 8B F9 E8 ?? ?? ?? ?? 8B 77 08 8B 6E 0C 83 46 08 01 8B CD E8")};

    // The model switch's first-person branch, ending in RemoveObject(0) on the full body: PUSH 0, MOV ECX,EDI,
    // CALL, which pops its own argument.
    FCSE::Relocation<uint8_t*> g_firstPersonBranch{FCSE::Pattern(
        "8B 4C 24 14 F7 D1 51 8B 48 34 E8 ?? ?? ?? ?? 6A 01 6A 01 8B CF E8 ?? ?? ?? ?? 6A 00 8B CF E8 ?? ?? ?? ?? "
        "5F 5E 5D 5B C2 04 00")};
    constexpr ptrdiff_t kDiscard = 0x1A;
    constexpr size_t kDiscardSize = 9;
}

namespace VehicleOverhaul::ViewMode {

bool Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_viewModeChanged || !g_firstPersonBranch) {
        api->Log("view mode: CPawn::ViewModeChanged or the model switch was not found in this build");
        return false;
    }
    uint8_t nops[kDiscardSize];
    for (uint8_t& nop : nops) {
        nop = 0x90;
    }
    return api->Patch(g_firstPersonBranch.get() + kDiscard, nops, kDiscardSize);
}

void Set(void* pawn, bool firstPerson) {
    if (pawn != nullptr && g_viewModeChanged) {
        g_viewModeChanged(pawn, firstPerson);
    }
}

}
