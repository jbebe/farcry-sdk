// The player's pawn. See docs/docs/engine-internals/first-person-aiming.md.
#include "engine/pawn.h"

#include "fcse_api.h"

namespace {
    // CPawnInputListener::Update, up to the call after which ECX is the live pawn. That call is
    // wildcarded because other plugins mid-hook it.
    FCSE::Relocation<uint8_t*> g_inputPass{FCSE::Pattern(
        "56 8B F1 74 0A 88 46 04 88 46 05 5E C2 08 00 8B 4E 20 3B C8 74 4A ?? ?? ?? ?? ?? F6 40 04 "
        "40 74 11")};
    constexpr size_t kLivePawn = 0x16;

    // CVehicle::GetCurrentVehicle: the vehicle a pawn sits in, or null.
    using GetCurrentVehicleFn = void*(__cdecl*)(uint8_t* pawn);
    FCSE::Relocation<GetCurrentVehicleFn> g_currentVehicle{FCSE::Uplay(0x000E7330)};

    PlayerBody::Pawn::TickFn g_tick = nullptr;

    void OnInputPass(FCSE_MidHookContext* ctx) {
        g_tick(reinterpret_cast<uint8_t*>(ctx->ecx));
    }
}

bool PlayerBody::Pawn::Install(TickFn tick) {
    if (!g_inputPass || !g_currentVehicle) {
        FCSE::ApiPointer()->Log("pawn: the gameplay input pass or the seat lookup was not found");
        return false;
    }
    g_tick = tick;
    return FCSE::ApiPointer()->MidHook(g_inputPass.get() + kLivePawn, &OnInputPass);
}

uint8_t* PlayerBody::Pawn::Entity(uint8_t* component) {
    return static_cast<uint8_t*>(FCSE::ApiPointer()->EntityData->EntityOf(component));
}

bool PlayerBody::Pawn::OnFoot(uint8_t* pawn) {
    return g_currentVehicle(pawn) == nullptr;
}
