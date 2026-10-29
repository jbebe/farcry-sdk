// Where a first-person shot leaves from, which can be moved for the player's.
//
// See docs/docs/engine-internals/first-person-aiming.md#where-a-first-person-shot-goes.
#include "engine/shot.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The bullet strategy's first-person shot, __thiscall(pawn, origin out, direction out, a
    // fourth it passes on): from body part 0, along its rotation and the spread.
    using SetupFn = void(__fastcall*)(uint8_t* strategy, void* unused, uint8_t* pawn, float* origin,
                                      float* direction, uint32_t fourth);
    FCSE::Relocation<SetupFn> g_setup{FCSE::Uplay(0x00112600)};
    SetupFn g_originalSetup = nullptr;

    AimingOverhaul::Shot::MoveFn g_move = nullptr;

    void __fastcall SetupDetour(uint8_t* strategy, void* unused, uint8_t* pawn, float* origin,
                                float* direction, uint32_t fourth) {
        g_originalSetup(strategy, unused, pawn, origin, direction, fourth);
        g_move(pawn, origin);
    }
}

bool AimingOverhaul::Shot::Install(MoveFn move) {
    g_move = move;
    if (!Hook(g_setup.address(), &SetupDetour, &g_originalSetup)) {
        FCSE::ApiPointer()->Log("shot: the first-person shot cannot be hooked, so a lean does not "
                                "move where it leaves from");
        return false;
    }
    return true;
}
