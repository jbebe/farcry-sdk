// Range falloff: a soldier with a pistol or SMG loses accuracy against the player with range, on top
// of the game's shared rifle curve; one with a shotgun is deadly out to 20 m and falls off fast past it.
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>

using namespace DifficultyOverhaul;

namespace {
    using DistanceFactorFn = float(__fastcall*)(void* shooting);

    // CShootingSystem::GetTargetDistanceFactor.
    FCSE::Relocation<DistanceFactorFn> g_getDistanceFactor{FCSE::Uplay(0x00A19950)};
    DistanceFactorFn g_originalGetDistanceFactor = nullptr;

    // Hardcore: the range at which a pistol's and an SMG's chance halves.
    constexpr float kPistolHalfRange = 20.0f;
    constexpr float kSMGHalfRange = 30.0f;
    // Hardcore: a shotgun's lift over the shared curve, the range it keeps it to, and how far past
    // that range its chance halves.
    constexpr float kShotgunLift = 1.15f;
    constexpr float kShotgunFullRange = 20.0f;
    constexpr float kShotgunHalfBeyond = 8.0f;

    // 1 at no distance, a half at `half`.
    float Halving(float distance, float half) {
        const float scaled = distance / half;
        return 1.0f / (1.0f + scaled * scaled);
    }

    // What the shared curve is multiplied by for a weapon class at `distance`.
    float Multiplier(int weaponClass, float distance) {
        switch (weaponClass) {
        case Shooting::kPistol:
            return Halving(distance, kPistolHalfRange);
        case Shooting::kSMG:
            return Halving(distance, kSMGHalfRange);
        case Shooting::kShotgun:
            return kShotgunLift *
                   Halving(std::max(distance - kShotgunFullRange, 0.0f), kShotgunHalfBeyond);
        default:
            return 1.0f;
        }
    }

    float __fastcall GetDistanceFactorDetour(void* shooting) {
        const float factor = g_originalGetDistanceFactor(shooting);
        if (!Shooting::AimsAtPlayer(shooting)) {
            return factor;
        }
        return factor *
               Multiplier(Shooting::WeaponClass(shooting), Shooting::TargetDistance(shooting));
    }
}

void InstallRangeFalloff() {
    if (!Shooting::Found() || !Shooting::WeaponClassFound() ||
        !Hook(g_getDistanceFactor, &GetDistanceFactorDetour, &g_originalGetDistanceFactor)) {
        FCSE::Logf("range falloff: the shooting system's range factor was not found in this build");
    }
}
