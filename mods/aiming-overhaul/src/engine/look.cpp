// The player's look, as the input builds it once a frame, with an offset of the plugin's own on it.
//
// See docs/docs/engine-internals/first-person-aiming.md#the-look.
#include "engine/look.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using AimingOverhaul::Field;
    using AimingOverhaul::Look::Angles;

    // CPawnInputListener::UpdateLook, __thiscall with no arguments.
    using UpdateLookFn = void(__fastcall*)(uint8_t* listener, void* unused);
    FCSE::Relocation<UpdateLookFn> g_updateLook{FCSE::Uplay(0x00143C40)};
    UpdateLookFn g_originalUpdateLook = nullptr;

    // In the pawn's data, the desired look in radians, pitch up and yaw left, kept twice.
    constexpr ptrdiff_t kDesiredPitch = 0x178;
    constexpr ptrdiff_t kDesiredYaw = 0x180;
    constexpr ptrdiff_t kDesiredAimPitch = 0x184;
    constexpr ptrdiff_t kDesiredAimYaw = 0x18C;

    AimingOverhaul::Look::OffsetFn g_offset = nullptr;
    uint8_t* g_pawn = nullptr;
    Angles g_applied = {};

    void __fastcall UpdateLookDetour(uint8_t* listener, void* unused) {
        g_originalUpdateLook(listener, unused);
        uint8_t* pawn = Field<uint8_t*>(listener, AimingOverhaul::Pawn::kListenerPawn);
        if (pawn == nullptr) {
            return;
        }
        if (pawn != g_pawn) {
            g_pawn = pawn;
            g_applied = {};
        }
        const Angles wanted = g_offset();
        const float up = wanted.up - g_applied.up;
        const float left = g_applied.right - wanted.right;
        g_applied = wanted;

        uint8_t* data = AimingOverhaul::Pawn::Data(pawn);
        Field<float>(data, kDesiredPitch) += up;
        Field<float>(data, kDesiredAimPitch) += up;
        Field<float>(data, kDesiredYaw) += left;
        Field<float>(data, kDesiredAimYaw) += left;
    }
}

bool AimingOverhaul::Look::Install(OffsetFn offset) {
    g_offset = offset;
    if (!Hook(g_updateLook.address(), &UpdateLookDetour, &g_originalUpdateLook)) {
        FCSE::ApiPointer()->Log("look: the look cannot be hooked, so a gun at a wall does not "
                                "swing down");
        return false;
    }
    return true;
}
