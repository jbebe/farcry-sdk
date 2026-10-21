// Whether a weapon can fire, which can be refused for the one in the player's hands.
#include "engine/fire.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    using AimingOverhaul::Field;

    // CWeaponState::CanFire(float), __thiscall, whose weapon is at the state's +0x00; and the
    // weapon's wielder, CEquipmentBase::GetWielder, the pawn holding it or null.
    using CanFireFn = bool(__fastcall*)(uint8_t* state, void* unused, float time);
    using WielderFn = uint8_t*(__fastcall*)(uint8_t* weapon);
    FCSE::Relocation<CanFireFn> g_canFire{FCSE::Uplay(0x0012EC50)};
    FCSE::Relocation<WielderFn> g_wielder{FCSE::Uplay(0x00156BA0)};
    CanFireFn g_originalCanFire = nullptr;

    AimingOverhaul::Fire::RefuseFn g_refuse = nullptr;

    bool __fastcall CanFireDetour(uint8_t* state, void* unused, float time) {
        if (!g_originalCanFire(state, unused, time)) {
            return false;
        }
        uint8_t* weapon = Field<uint8_t*>(state, 0);
        return weapon == nullptr || !g_refuse(g_wielder(weapon));
    }
}

bool AimingOverhaul::Fire::Install(RefuseFn refuse) {
    g_refuse = refuse;
    if (!g_wielder || !Hook(g_canFire.address(), &CanFireDetour, &g_originalCanFire)) {
        FCSE::ApiPointer()->Log("fire: the weapon's fire check cannot be hooked, so a gun at a wall "
                                "still fires");
        return false;
    }
    return true;
}
