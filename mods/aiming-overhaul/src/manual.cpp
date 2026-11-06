// The operation manual. See manual.h and
// docs/docs/engine-internals/first-person-aiming.md#the-accuracy-bonus.
#include "manual.h"

#include "engine/memory.h"
#include "engine/shot.h"
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using AimingOverhaul::Field;

    // CPawn::GetBonus, __thiscall: `base` with the pawn's bonus plans of that name for that object.
    using BonusFn = float(__fastcall*)(uint8_t* pawn, void* unused, const uint32_t* object,
                                       const uint32_t* name, float base, int lowerIsBetter);
    // CWeaponFireBulletStrategy::GetActualSpreadData, __thiscall(pawn): the shot's spread data.
    using SpreadDataFn = uint8_t*(__fastcall*)(uint8_t* strategy, void* unused, uint8_t* pawn);
    FCSE::Relocation<BonusFn> g_bonus{FCSE::Uplay(0x0007E280)};
    FCSE::Relocation<SpreadDataFn> g_spreadData{FCSE::Uplay(0x0010EC30)};
    BonusFn g_original = nullptr;

    constexpr ptrdiff_t kFireData = 0x50;
    // In the fire data, the spread down the iron sights standing and crouched.
    constexpr ptrdiff_t kIronSight = 0x30C;
    constexpr ptrdiff_t kCrouchIronSight = 0x378;

    // The bonus a shot's noise is narrowed by.
    constexpr uint32_t kAccuracy = FCSE::Crc32("accuracy");
    // The most of the noise a gun's manual leaves; its challenge alone leaves 0.9.
    constexpr float kWithManual = 0.85f;
    constexpr float kStanding = 0.5f;
    constexpr float kCrouched = 0.25f;

    float __fastcall BonusDetour(uint8_t* pawn, void* unused, const uint32_t* object,
                                 const uint32_t* name, float base, int lowerIsBetter) {
        uint8_t* strategy = *name == kAccuracy ? AimingOverhaul::Shot::SettingUp() : nullptr;
        if (strategy == nullptr) {
            return g_original(pawn, unused, object, name, base, lowerIsBetter);
        }
        const float value = g_original(pawn, unused, object, name, base, lowerIsBetter);
        if (value <= 0.0f || value > base * kWithManual) {
            return value;
        }
        const ptrdiff_t picked =
            g_spreadData(strategy, nullptr, pawn) - Field<uint8_t*>(strategy, kFireData);
        if (picked == kIronSight) {
            return base * kStanding;
        }
        if (picked == kCrouchIronSight) {
            return base * kCrouched;
        }
        return value;
    }
}

bool AimingOverhaul::Manual::Install() {
    if (!g_spreadData || !Hook(g_bonus.address(), &BonusDetour, &g_original)) {
        FCSE::ApiPointer()->Log("manual: the bonus plans cannot be hooked, so an operation manual "
                                "narrows a gun's spread as the game does");
        return false;
    }
    return true;
}
