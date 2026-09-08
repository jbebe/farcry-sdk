// Invincibility.
//
// `cheat_GodMode` sets one profile flag, and that flag is only the first of three things standing
// between the player and dying. It is re-applied every frame here rather than set once, because a
// profile reload puts the file's value back.
//
// The second is the health floor. GodMode stops damage but does not lift the player back above the
// health-failure threshold, so a player already hurt when it is switched on stays one scratch from
// going down. The floor watches the campaign player's vitals and tops the health counter back up
// through CCounter::SetToMax, which is a virtual call rather than a value write because the HUD and
// event chain hang off it. Three states park the player below the threshold on purpose - a scripted
// forced failure, a buddy rescue, and revive invulnerability - and the floor leaves all three alone,
// so a rescue still plays out.
//
// The third is vehicles, which GodMode reaches not at all: a jeep taking an RPG is a different
// damage path entirely, and the two gate bytes on the component that would suppress it are
// recomputed every physics step, so there is nothing to patch. Both entry points are hooked instead,
// and the player's own vehicle is re-derived each frame - the pawn keeps no pointer to it, so the
// link has to be walked from the seat fact through to the physics component.
//
// Drowning and scripted destruction stay lethal.
#include "engine/entity.h"
#include "engine/game_profile.h"
#include "engine/pawn_tick.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    // CFCXCountersComponentPlayerSP, the campaign player's vitals.
    constexpr ptrdiff_t kCountersHealth = 0x44;
    constexpr ptrdiff_t kCountersFailureThreshold = 0x6C;
    constexpr ptrdiff_t kCountersForcedFailure = 0x88;
    constexpr ptrdiff_t kCountersRescueState = 0xE8;
    constexpr ptrdiff_t kCountersReviveInvulnerable = 0x140;

    constexpr ptrdiff_t kCounterValue = 0x10;
    constexpr size_t kCounterSetToMaxSlot = 0x20 / sizeof(void*);

    using CounterSetToMaxFn = void(__fastcall*)(void* counter);

    // The constructor, entered past both vtable stores so ESI is the finished object.
    FCSE::Relocation<uint8_t*> g_countersCtor{FCSE::Pattern(
        "F3 0F 11 8E 10 01 00 00 C7 06 ?? ?? ?? ?? C7 46 04 ?? ?? ?? ?? F3 0F 11 86 F4 00 00 00")};

    constexpr size_t kCountersCtorVTableSet = 0x15;

    bool g_enabled = false;

    void* g_counters = nullptr;
    void* g_countersVTable = nullptr;

    void CountersSighted(FCSE_MidHookContext* ctx) {
        g_counters = reinterpret_cast<void*>(ctx->esi);
        g_countersVTable = *reinterpret_cast<void**>(ctx->esi);
    }

    void ApplyHealthFloor(void* local) {
        if (!g_enabled || !DevTools::Player::ObjectIsLive(g_counters, g_countersVTable, local)) {
            return;
        }

        auto* counters = static_cast<uint8_t*>(g_counters);

        if (*reinterpret_cast<uint8_t*>(counters + kCountersForcedFailure) != 0 ||
            *reinterpret_cast<int32_t*>(counters + kCountersRescueState) != 0 ||
            *reinterpret_cast<uint8_t*>(counters + kCountersReviveInvulnerable) != 0) {
            return;
        }

        auto* counter = *reinterpret_cast<uint8_t**>(counters + kCountersHealth);
        if (counter == nullptr) {
            return;
        }

        // The threshold is 80.0 from the constructor but raised at runtime, so it is read each time.
        if (*reinterpret_cast<float*>(counter + kCounterValue) >=
            *reinterpret_cast<float*>(counters + kCountersFailureThreshold)) {
            return;
        }

        auto** vtable = *reinterpret_cast<void***>(counter);
        reinterpret_cast<CounterSetToMaxFn>(vtable[kCounterSetToMaxSlot])(counter);
    }

    // CVehiclePhysComponent+0x08 is a ref block laid out like an entity ref holder, so the entity
    // comes off it through Entity::Of.
    constexpr ptrdiff_t kVehicleRefBlock = 0x08;

    using GetCurrentVehicleFn = void*(__cdecl*)(void* pawn);
    using FlushEntityJobFn = void(__fastcall*)(void* entity);
    using GetVehiclePhysicsFn = void*(__fastcall*)(void* entity);
    using VehicleHealthDamageFn = void(__fastcall*)(void* component, void* unused, float damage);
    using VehicleStimPartsFn = void(__fastcall*)(void* component, void* unused, void* stim);

    FCSE::Relocation<GetCurrentVehicleFn> g_getCurrentVehicle{FCSE::Uplay(0x000E7330)};
    FCSE::Relocation<FlushEntityJobFn> g_flushEntityJob{FCSE::Uplay(0x004DD4F0)};

    // The address library rather than a pattern: this getter's bytes are shared with 69 others, and
    // telling them apart needs the unwildcarded call displacement, which differs between builds.
    FCSE::Relocation<GetVehiclePhysicsFn> g_getVehiclePhysics{FCSE::Uplay(0x000649F0)};

    FCSE::Relocation<VehicleHealthDamageFn> g_vehicleHealthDamage{FCSE::Uplay(0x00067170)};
    FCSE::Relocation<VehicleStimPartsFn> g_vehicleStimParts{FCSE::Uplay(0x00071450)};

    VehicleHealthDamageFn g_originalHealthDamage = nullptr;
    VehicleStimPartsFn g_originalStimParts = nullptr;

    // Read from the damage hooks on physics jobs, so written last-to-first and cleared
    // first-to-last: a torn read has to fail the component test rather than half-pass it.
    void* g_vehiclePhysics = nullptr;
    void* g_vehicleRefBlock = nullptr;
    void* g_vehicleEntity = nullptr;

    // The entity the derivation last ran for, so it only runs again on entering or leaving a seat.
    void* g_derivedFor = nullptr;

    bool VehicleIsProtected(void* component) {
        if (!g_enabled || component == nullptr || component != g_vehiclePhysics) {
            return false;
        }

        auto* refBlock = *reinterpret_cast<void**>(static_cast<uint8_t*>(component) + kVehicleRefBlock);
        if (refBlock == nullptr || refBlock != g_vehicleRefBlock) {
            return false;
        }

        return DevTools::Entity::Of(refBlock) == g_vehicleEntity;
    }

    void Forget() {
        g_vehiclePhysics = nullptr;
        g_vehicleRefBlock = nullptr;
        g_vehicleEntity = nullptr;
        g_derivedFor = nullptr;
    }

    void ApplyVehicleProtection(uintptr_t pawn) {
        if (!g_enabled || pawn == 0 || !g_getCurrentVehicle || !g_getVehiclePhysics) {
            if (g_derivedFor != nullptr) {
                Forget();
            }
            return;
        }

        void* vehicle = g_getCurrentVehicle.get()(reinterpret_cast<void*>(pawn));
        void* seatBlock =
            vehicle != nullptr
                ? *reinterpret_cast<void**>(static_cast<uint8_t*>(vehicle) + kVehicleRefBlock)
                : nullptr;
        void* entity = seatBlock != nullptr ? DevTools::Entity::Of(seatBlock) : nullptr;

        // Already derived for this entity. The job flush below is a sync point, so it is worth not
        // paying for it on every frame of a drive; the damage hooks re-validate the block anyway.
        if (entity != nullptr && entity == g_derivedFor) {
            return;
        }

        Forget();
        if (entity == nullptr) {
            return;
        }

        if (g_flushEntityJob) {
            g_flushEntityJob.get()(entity);
        }

        void* physics = g_getVehiclePhysics.get()(entity);
        if (physics == nullptr) {
            return;
        }

        // Read back off the physics component, so the hooks compare against the block they read
        // themselves rather than the one the seat happened to name.
        auto* refBlock = *reinterpret_cast<void**>(static_cast<uint8_t*>(physics) + kVehicleRefBlock);
        void* physicsEntity = refBlock != nullptr ? DevTools::Entity::Of(refBlock) : nullptr;
        if (physicsEntity == nullptr) {
            return;
        }

        g_vehicleRefBlock = refBlock;
        g_vehicleEntity = physicsEntity;
        g_vehiclePhysics = physics;
        g_derivedFor = entity;
    }

    // Zero passed rather than the call skipped, so the health ratio at +0x90 is still recomputed.
    void __fastcall VehicleHealthDamageDetour(void* component, void* unused, float damage) {
        g_originalHealthDamage(component, unused, VehicleIsProtected(component) ? 0.0f : damage);
    }

    // Part breakage, safe to skip outright: both callers ignore the return, and the collision
    // immunity timer it sets only throttles damage that is already suppressed.
    void __fastcall VehicleStimPartsDetour(void* component, void* unused, void* stim) {
        if (!VehicleIsProtected(component)) {
            g_originalStimParts(component, unused, stim);
        }
    }

    void OnTick(const DevTools::PawnTick::Frame& frame) {
        ApplyHealthFloor(frame.local);
        ApplyVehicleProtection(frame.pawn);
    }
}

void InstallInvincibilityHooks() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    DevTools::PawnTick::Subscribe(&OnTick);

    if (g_countersCtor) {
        api->MidHook(g_countersCtor.get() + kCountersCtorVTableSet, &CountersSighted);
    } else {
        api->Log("invincibility: the player's vitals were not found in this build - god mode will "
                 "stop damage but will not lift the player back above the failure threshold");
    }

    if (g_vehicleHealthDamage && g_vehicleStimParts) {
        api->Hook(reinterpret_cast<void*>(g_vehicleHealthDamage.address()),
                  reinterpret_cast<void*>(&VehicleHealthDamageDetour),
                  reinterpret_cast<void**>(&g_originalHealthDamage));
        api->Hook(reinterpret_cast<void*>(g_vehicleStimParts.address()),
                  reinterpret_cast<void*>(&VehicleStimPartsDetour),
                  reinterpret_cast<void**>(&g_originalStimParts));
    } else {
        api->Log("invincibility: the vehicle damage paths were not found in this build - vehicles "
                 "take damage as they shipped");
    }
}

int GetInvincibility() { return g_enabled ? 1 : 0; }

void SetInvincibility(int value) {
    if ((value != 0) == g_enabled) {
        return;
    }

    g_enabled = value != 0;
    DevTools::GameProfile::Want(DevTools::GameProfile::Cheat::GodMode, g_enabled);

    FCSE::ApiPointer()->Log(g_enabled ? "invincibility: on" : "invincibility: off");
}
