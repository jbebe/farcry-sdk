// The kick each shot gives a pawn's aim, which can be taken from the game.
//
// See docs/docs/engine-internals/first-person-aiming.md#recoil.
#include "engine/kick.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using AimingOverhaul::Field;

    // CPawnBeautifierFirst::UpdateRecoil, __thiscall(float seconds), and the beautifier's pawn.
    using UpdateRecoilFn = void(__fastcall*)(uint8_t* beautifier, void* unused, float seconds);
    FCSE::Relocation<UpdateRecoilFn> g_updateRecoil{FCSE::Uplay(0x000A0050)};
    UpdateRecoilFn g_originalUpdateRecoil = nullptr;
    constexpr ptrdiff_t kBeautifierPawn = 0x10;

    // In the pawn's data, the recoil as a shot last asked for it, and as it stands: pitch up and yaw
    // left in radians, and in the desired one the seconds to reach it and whether a shot has just
    // asked.
    constexpr ptrdiff_t kDesiredUp = 0x214;
    constexpr ptrdiff_t kDesiredLeft = 0x218;
    constexpr ptrdiff_t kDesiredSeconds = 0x21C;
    constexpr ptrdiff_t kDesiredKicked = 0x220;
    constexpr ptrdiff_t kEffectiveUp = 0x3A4;
    constexpr ptrdiff_t kEffectiveLeft = 0x3A8;

    AimingOverhaul::Kick::TakeFn g_take = nullptr;

    // A shot asks for the recoil as it stands plus its kick. A kick taken leaves the recoil where it
    // stands.
    void __fastcall UpdateRecoilDetour(uint8_t* beautifier, void* unused, float seconds) {
        uint8_t* pawn = Field<uint8_t*>(beautifier, kBeautifierPawn);
        uint8_t* data = pawn != nullptr ? AimingOverhaul::Pawn::Data(pawn) : nullptr;
        if (data != nullptr && Field<uint8_t>(data, kDesiredKicked) != 0) {
            const float up = Field<float>(data, kEffectiveUp);
            const float left = Field<float>(data, kEffectiveLeft);
            const AimingOverhaul::Kick::Kick kick = {Field<float>(data, kDesiredUp) - up,
                                                     left - Field<float>(data, kDesiredLeft),
                                                     Field<float>(data, kDesiredSeconds)};
            if (g_take(pawn, kick)) {
                Field<float>(data, kDesiredUp) = up;
                Field<float>(data, kDesiredLeft) = left;
                Field<uint8_t>(data, kDesiredKicked) = 0;
            }
        }
        g_originalUpdateRecoil(beautifier, unused, seconds);
    }
}

bool AimingOverhaul::Kick::Install(TakeFn take) {
    g_take = take;
    if (!Hook(g_updateRecoil.address(), &UpdateRecoilDetour, &g_originalUpdateRecoil)) {
        FCSE::ApiPointer()->Log("kick: the recoil cannot be hooked, so the game takes each kick "
                                "back");
        return false;
    }
    return true;
}
