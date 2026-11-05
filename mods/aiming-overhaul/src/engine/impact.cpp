// Each bullet's impact on a surface, as the engine spawns its puff.
//
// See docs/docs/engine-internals/audio-runtime.md#fly-bys.
#include "engine/impact.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstddef>

namespace {
    using AimingOverhaul::Field;

    // CWeaponFireBulletProperties::SpawnBulletPuff, __thiscall(the shooter's entity proxy, the
    // impact, a flag), which lets go of its hold on the proxy.
    using SpawnPuffFn = void(__fastcall*)(uint8_t* properties, void* unused, uint8_t* shooter,
                                          float* impact, char flag);
    FCSE::Relocation<SpawnPuffFn> g_spawnPuff{FCSE::Uplay(0x000FAAB0)};
    SpawnPuffFn g_original = nullptr;

    // In the impact, its point first and its logic material.
    constexpr ptrdiff_t kImpactMaterial = 0x21;

    AimingOverhaul::Impact::ImpactFn g_onImpact = nullptr;

    void __fastcall SpawnPuffDetour(uint8_t* properties, void* unused, uint8_t* shooter,
                                    float* impact, char flag) {
        if (impact != nullptr) {
            g_onImpact(impact,
                       Field<uint8_t>(reinterpret_cast<uint8_t*>(impact), kImpactMaterial),
                       AimingOverhaul::ProxyEntity(shooter));
        }
        g_original(properties, unused, shooter, impact, flag);
    }
}

bool AimingOverhaul::Impact::Install(ImpactFn onImpact) {
    g_onImpact = onImpact;
    if (!Hook(g_spawnPuff.address(), &SpawnPuffDetour, &g_original)) {
        FCSE::ApiPointer()->Log("impact: the bullets' impacts cannot be hooked, so no dirt flies "
                                "in the eye");
        return false;
    }
    return true;
}
