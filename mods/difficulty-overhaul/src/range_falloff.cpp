// Range falloff: a soldier with a pistol, SMG or shotgun loses accuracy against the player with range,
// on top of the game's shared rifle curve.
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

using namespace DifficultyOverhaul;

namespace {
    using DistanceFactorFn = float(__fastcall*)(void* shooting);
    using GetEquippedWeaponFn = void*(__fastcall*)(void* inventory);

    // CShootingSystem::GetTargetDistanceFactor and CInventoryViewPawn::GetEquippedWeapon.
    FCSE::Relocation<DistanceFactorFn> g_getDistanceFactor{FCSE::Uplay(0x00A19950)};
    FCSE::Relocation<GetEquippedWeaponFn> g_getEquippedWeapon{FCSE::Uplay(0x00127DA0)};
    DistanceFactorFn g_originalGetDistanceFactor = nullptr;

    // The inventory view in a CPawn's data block; CWeapon's properties holder, the
    // CWeaponPropertiesCommon in it, and its selWeaponClass.
    constexpr ptrdiff_t kDataInventory = 0x4F0;
    constexpr ptrdiff_t kWeaponPropertiesHolder = 0x24;
    constexpr ptrdiff_t kHolderProperties = 0x04;
    constexpr ptrdiff_t kPropertiesClass = 0x40;
    enum { kPistol = 1, kSMG = 3, kShotgun = 4 };

    // Hardcore: the range at which each class's chance halves.
    constexpr float kPistolHalfRange = 20.0f;
    constexpr float kSMGHalfRange = 30.0f;
    constexpr float kShotgunHalfRange = 15.0f;

    // The selWeaponClass of the weapon he holds, or -1.
    int WeaponClass(void* shooting) {
        void* inventory = Shooting::PawnData(Shooting::ShooterPawn(shooting)) + kDataInventory;
        void* weapon = g_getEquippedWeapon(inventory);
        if (weapon == nullptr) {
            return -1;
        }
        const void* holder = Read<void*>(weapon, kWeaponPropertiesHolder);
        return Read<int>(Read<void*>(holder, kHolderProperties), kPropertiesClass);
    }

    // The range a class's chance halves at, or 0 for a class that keeps the shared curve.
    float HalfRange(int weaponClass) {
        switch (weaponClass) {
        case kPistol:
            return kPistolHalfRange;
        case kSMG:
            return kSMGHalfRange;
        case kShotgun:
            return kShotgunHalfRange;
        default:
            return 0.0f;
        }
    }

    float __fastcall GetDistanceFactorDetour(void* shooting) {
        const float factor = g_originalGetDistanceFactor(shooting);
        const float half =
            Shooting::AimsAtPlayer(shooting) ? HalfRange(WeaponClass(shooting)) : 0.0f;
        if (half == 0.0f) {
            return factor;
        }
        const float scaled = Shooting::TargetDistance(shooting) / half;
        return factor / (1.0f + scaled * scaled);
    }
}

void InstallRangeFalloff() {
    if (!Shooting::Found() || !g_getEquippedWeapon ||
        !Hook(g_getDistanceFactor, &GetDistanceFactorDetour, &g_originalGetDistanceFactor)) {
        FCSE::Logf("range falloff: the shooting system's range factor was not found in this build");
    }
}
