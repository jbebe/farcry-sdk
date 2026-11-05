// Each hit on the player, as the engine deals it.
//
// See docs/docs/engine-internals/stims-and-damage.md#the-player.
#include "engine/damage.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using AimingOverhaul::Field;

    // CFCXCountersComponentPlayer::HandleDamageStim, __thiscall(the stim).
    using HandleDamageFn = void(__fastcall*)(uint8_t* counters, void* unused, uint8_t* stim);
    FCSE::Relocation<HandleDamageFn> g_handleDamage{FCSE::Uplay(0x006A2880)};
    HandleDamageFn g_original = nullptr;

    // CEntityEventStims: its type, of which pierce, crush and cut are a weapon's, and the body parts
    // it hit. A torso bit outranks the head's, as the engine's own location multiplier tests them.
    constexpr ptrdiff_t kStimType = 0x48;
    constexpr ptrdiff_t kStimBodyParts = 0x5C;
    constexpr int kPierce = 3;
    constexpr int kCut = 5;
    constexpr uint32_t kTorso = 0x003;
    constexpr uint32_t kHead = 0x040;

    AimingOverhaul::Damage::HitFn g_onHit = nullptr;

    void __fastcall HandleDamageDetour(uint8_t* counters, void* unused, uint8_t* stim) {
        const float before = AimingOverhaul::Pawn::HealthOf(counters);
        g_original(counters, unused, stim);
        const float lost = before - AimingOverhaul::Pawn::HealthOf(counters);
        const int type = Field<int>(stim, kStimType);
        if (lost > 0.0f && type >= kPierce && type <= kCut) {
            const uint32_t parts = Field<uint32_t>(stim, kStimBodyParts);
            g_onHit((parts & kTorso) == 0 && (parts & kHead) != 0, lost);
        }
    }
}

bool AimingOverhaul::Damage::Install(HitFn onHit) {
    g_onHit = onHit;
    if (!Hook(g_handleDamage.address(), &HandleDamageDetour, &g_original)) {
        FCSE::ApiPointer()->Log("damage: the player's damage cannot be hooked, so a hit shows "
                                "nothing");
        return false;
    }
    return true;
}
