// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
//
// See docs/docs/engine-internals/first-person-aiming.md.
#include "engine/aim.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>

namespace {
    // All __thiscall, which a free function spells __fastcall with an unused EDX.
    using UpdateFn = void(__fastcall*)(uint8_t* camera, void* unused, float seconds, uint32_t flags);
    using UpdateCameraOffsetFn = void(__fastcall*)(uint8_t* camera, void* unused, float seconds,
                                                   uint8_t* pawn);
    using UpdateLookFn = void(__fastcall*)(uint8_t* listener, void* unused);
    using EquippedWeaponFn = uint8_t*(__fastcall*)(uint8_t* inventory);
    using PlayerCameraFn = uint8_t*(__cdecl*)();

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x00693C00)};
    FCSE::Relocation<UpdateCameraOffsetFn> g_updateCameraOffset{FCSE::Uplay(0x00693490)};
    FCSE::Relocation<UpdateLookFn> g_updateLook{FCSE::Uplay(0x00143C40)};
    FCSE::Relocation<EquippedWeaponFn> g_equippedWeapon{FCSE::Uplay(0x00127DA0)};
    FCSE::Relocation<PlayerCameraFn> g_playerCamera{FCSE::Uplay(0x0070C210)};

    UpdateFn g_originalUpdate = nullptr;
    UpdateCameraOffsetFn g_originalUpdateCameraOffset = nullptr;
    UpdateLookFn g_originalUpdateLook = nullptr;

    // CCameraPawnComponent: the eye's offset in the view's axes, x right, y ahead, z up.
    constexpr ptrdiff_t kEyeRight = 0xE8;
    constexpr ptrdiff_t kEyeUp = 0xF0;

    // CPawn's data, and in it the effective flags, the desired look in radians, and the inventory.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kEffectiveFlags = 0x2D4;
    constexpr uint8_t kIronsight = 0x08;
    constexpr ptrdiff_t kDesiredPitch[] = {0x178, 0x184};
    constexpr ptrdiff_t kDesiredYaw[] = {0x180, 0x18C};
    constexpr ptrdiff_t kInventory = 0x4F0;

    // CPawnInputListener's pawn.
    constexpr ptrdiff_t kListenerPawn = 0x20;

    // CFCXWeapon: the scope picture as last asked for, and whether the weapon has one.
    constexpr ptrdiff_t kScopeShown = 0x84;
    constexpr ptrdiff_t kHiResScope = 0x85;

    // Seconds to settle into the sights, and to let go of them.
    constexpr float kSettle = 0.4f;
    // Through a scope the drift turns the rifle, and the aim with it, as if about the shoulder.
    constexpr float kRadiansPerMetre = 2.5f;

    WeaponOverhaul::Aim::DriftFn g_drift = nullptr;
    float g_sights = 0.0f;
    float g_scope = 0.0f;
    float g_scopeTurn = 0.0f;
    std::atomic<float> g_settled{0.0f};
    std::atomic<float> g_scoped{0.0f};
    std::atomic<float> g_driftRight{0.0f};
    std::atomic<float> g_driftUp{0.0f};

    // What was added to the eye during the camera update under way, and to which camera.
    uint8_t* g_addedTo = nullptr;
    WeaponOverhaul::Aim::Offset g_added = {};

    // The turn the look should carry, and what it carries already, to which pawn.
    float g_turnPitch = 0.0f;
    float g_turnYaw = 0.0f;
    uint8_t* g_turnedPawn = nullptr;
    float g_turnedPitch = 0.0f;
    float g_turnedYaw = 0.0f;

    template <typename T>
    T& Field(uint8_t* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(object + offset);
    }

    float Ease(float linear) {
        return linear * linear * (3.0f - 2.0f * linear);
    }

    float Approach(float value, bool toward, float step) {
        return std::clamp(value + (toward ? step : -step), 0.0f, 1.0f);
    }

    bool ScopeShown(uint8_t* pawnData) {
        uint8_t* weapon = g_equippedWeapon(pawnData + kInventory);
        return weapon != nullptr && Field<uint8_t>(weapon, kScopeShown) != 0 &&
               Field<uint8_t>(weapon, kHiResScope) != 0;
    }

    void __fastcall UpdateCameraOffsetDetour(uint8_t* camera, void* unused, float seconds,
                                             uint8_t* pawn) {
        g_originalUpdateCameraOffset(camera, unused, seconds, pawn);
        if (camera != g_playerCamera()) {
            return;
        }

        uint8_t* data = Field<uint8_t*>(pawn, kPawnData);
        const bool sights = (Field<uint8_t>(data, kEffectiveFlags) & kIronsight) != 0;
        const bool scope = sights && ScopeShown(data);
        const float step = seconds / kSettle;
        // The iron sights let go at once when a scope's own sight picture comes up, and the scope
        // when it goes; the turn it gave the aim eases back.
        g_sights = scope ? 0.0f : Approach(g_sights, sights, step);
        g_scope = scope ? Approach(g_scope, true, step) : 0.0f;
        g_scopeTurn = Approach(g_scopeTurn, scope, step);
        const float settled = Ease(g_sights);
        const float turn = Ease(g_scopeTurn) * kRadiansPerMetre;
        g_settled = settled;
        g_scoped = Ease(g_scope);

        const WeaponOverhaul::Aim::Offset drift = g_drift(seconds);
        g_driftRight = drift.right;
        g_driftUp = drift.up;
        g_added = {drift.right * settled, drift.up * settled};
        Field<float>(camera, kEyeRight) += g_added.right;
        Field<float>(camera, kEyeUp) += g_added.up;
        g_addedTo = camera;

        // Pitch is up, and yaw turns left.
        g_turnPitch = drift.up * turn;
        g_turnYaw = -drift.right * turn;
    }

    void __fastcall UpdateDetour(uint8_t* camera, void* unused, float seconds, uint32_t flags) {
        g_originalUpdate(camera, unused, seconds, flags);
        if (g_addedTo != nullptr) {
            Field<float>(g_addedTo, kEyeRight) -= g_added.right;
            Field<float>(g_addedTo, kEyeUp) -= g_added.up;
            g_addedTo = nullptr;
        }
    }

    // The look is built anew each frame from where it last was, so the turn goes on as it changes.
    void __fastcall UpdateLookDetour(uint8_t* listener, void* unused) {
        g_originalUpdateLook(listener, unused);
        uint8_t* pawn = Field<uint8_t*>(listener, kListenerPawn);
        if (pawn != g_turnedPawn) {
            g_turnedPawn = pawn;
            g_turnedPitch = 0.0f;
            g_turnedYaw = 0.0f;
        }
        if (pawn == nullptr) {
            return;
        }
        uint8_t* data = Field<uint8_t*>(pawn, kPawnData);
        for (ptrdiff_t pitch : kDesiredPitch) {
            Field<float>(data, pitch) += g_turnPitch - g_turnedPitch;
        }
        for (ptrdiff_t yaw : kDesiredYaw) {
            Field<float>(data, yaw) += g_turnYaw - g_turnedYaw;
        }
        g_turnedPitch = g_turnPitch;
        g_turnedYaw = g_turnYaw;
    }

    bool Hook(const FCSE_PluginAPI* api, uintptr_t target, void* detour, void* original) {
        return api->Hook(reinterpret_cast<void*>(target), detour,
                         reinterpret_cast<void**>(original));
    }
}

bool WeaponOverhaul::Aim::Install(DriftFn drift) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_update || !g_updateCameraOffset || !g_updateLook || !g_equippedWeapon ||
        !g_playerCamera) {
        api->Log("aim: the camera functions were not found in this build");
        return false;
    }

    g_drift = drift;
    if (!Hook(api, g_update.address(), reinterpret_cast<void*>(&UpdateDetour),
              &g_originalUpdate)) {
        return false;
    }
    // Past the first hook a refusal would unload the plugin with that hook live, so these only log.
    if (!Hook(api, g_updateCameraOffset.address(),
              reinterpret_cast<void*>(&UpdateCameraOffsetDetour), &g_originalUpdateCameraOffset)) {
        api->Log("aim: the camera offset cannot be hooked, so nothing follows the sights");
    } else if (!Hook(api, g_updateLook.address(), reinterpret_cast<void*>(&UpdateLookDetour),
                     &g_originalUpdateLook)) {
        api->Log("aim: the look cannot be hooked, so a scope does not sway");
    }
    return true;
}

float WeaponOverhaul::Aim::Settled() {
    return g_settled;
}

float WeaponOverhaul::Aim::Scoped() {
    return g_scoped;
}

WeaponOverhaul::Aim::Offset WeaponOverhaul::Aim::Drift() {
    return {g_driftRight, g_driftUp};
}
