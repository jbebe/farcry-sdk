// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
//
// See docs/docs/engine-internals/first-person-aiming.md.
#include "engine/aim.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <numbers>

namespace {
    // All __thiscall, which a free function spells __fastcall with an unused EDX.
    using UpdateFn = void(__fastcall*)(uint8_t* camera, void* unused, float seconds, uint32_t flags);
    using UpdateCameraOffsetFn = void(__fastcall*)(uint8_t* camera, void* unused, float seconds,
                                                   uint8_t* pawn);
    using EquippedWeaponFn = uint8_t*(__fastcall*)(uint8_t* inventory);
    using PlayerCameraFn = uint8_t*(__cdecl*)();

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x00693C00)};
    FCSE::Relocation<UpdateCameraOffsetFn> g_updateCameraOffset{FCSE::Uplay(0x00693490)};
    FCSE::Relocation<EquippedWeaponFn> g_equippedWeapon{FCSE::Uplay(0x00127DA0)};
    FCSE::Relocation<PlayerCameraFn> g_playerCamera{FCSE::Uplay(0x0070C210)};

    UpdateFn g_originalUpdate = nullptr;
    UpdateCameraOffsetFn g_originalUpdateCameraOffset = nullptr;

    // CCameraPawnComponent: the eye's offset in the view's axes, x right, y ahead, z up.
    constexpr ptrdiff_t kEyeRight = 0xE8;
    constexpr ptrdiff_t kEyeUp = 0xF0;

    // CPawn's data, and in it the effective flags, the effective look in radians (pitch up, yaw
    // left), and the inventory.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kEffectiveFlags = 0x2D4;
    constexpr uint8_t kIronsight = 0x08;
    constexpr ptrdiff_t kEffectivePitch = 0x308;
    constexpr ptrdiff_t kEffectiveYaw = 0x310;
    constexpr ptrdiff_t kInventory = 0x4F0;

    // CFCXWeapon: the scope picture as last asked for, and whether the weapon has one.
    constexpr ptrdiff_t kScopeShown = 0x84;
    constexpr ptrdiff_t kHiResScope = 0x85;

    // Seconds to settle into the sights, and to let go of them.
    constexpr float kSettle = 0.4f;
    // Seconds for the turn rate to follow the look, which is how the gun trails it. Faster than
    // this in a frame is a cut, not a turn.
    constexpr float kTrail = 0.12f;
    constexpr float kFastestTurn = 10.0f;

    WeaponOverhaul::Aim::DriftFn g_drift = nullptr;
    float g_sights = 0.0f;
    float g_scope = 0.0f;
    std::atomic<float> g_settled{0.0f};
    std::atomic<float> g_scoped{0.0f};

    // What was added to the eye during the camera update under way, and to which camera.
    uint8_t* g_addedTo = nullptr;
    WeaponOverhaul::Aim::Offset g_added = {};

    // The look last frame, for which pawn, and how fast it has been turning.
    uint8_t* g_lookPawn = nullptr;
    float g_lastPitch = 0.0f;
    float g_lastYaw = 0.0f;
    WeaponOverhaul::Aim::Turn g_turn = {};
    std::atomic<float> g_turnRight{0.0f};
    std::atomic<float> g_turnUp{0.0f};

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

    // Follows the look's turn rate, smoothed the way the gun trails the camera.
    void FollowTurn(uint8_t* pawn, uint8_t* data, float seconds) {
        const float pitch = Field<float>(data, kEffectivePitch);
        const float yaw = Field<float>(data, kEffectiveYaw);
        if (pawn != g_lookPawn || seconds <= 0.0f) {
            g_lookPawn = pawn;
            g_lastPitch = pitch;
            g_lastYaw = yaw;
            return;
        }
        const float pi = std::numbers::pi_v<float>;
        const float yawStep = std::remainder(yaw - g_lastYaw, 2.0f * pi);
        const float right = std::clamp(-yawStep / seconds, -kFastestTurn, kFastestTurn);
        const float up = std::clamp((pitch - g_lastPitch) / seconds, -kFastestTurn, kFastestTurn);
        g_lastPitch = pitch;
        g_lastYaw = yaw;

        const float follow = 1.0f - std::exp(-seconds / kTrail);
        g_turn.right += (right - g_turn.right) * follow;
        g_turn.up += (up - g_turn.up) * follow;
        g_turnRight = g_turn.right;
        g_turnUp = g_turn.up;
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
        // when it goes.
        g_sights = scope ? 0.0f : Approach(g_sights, sights, step);
        g_scope = scope ? Approach(g_scope, true, step) : 0.0f;
        const float settled = Ease(g_sights);
        g_settled = settled;
        g_scoped = Ease(g_scope);
        FollowTurn(pawn, data, seconds);

        const WeaponOverhaul::Aim::Offset drift = g_drift(seconds);
        g_added = {drift.right * settled, drift.up * settled};
        Field<float>(camera, kEyeRight) += g_added.right;
        Field<float>(camera, kEyeUp) += g_added.up;
        g_addedTo = camera;
    }

    void __fastcall UpdateDetour(uint8_t* camera, void* unused, float seconds, uint32_t flags) {
        g_originalUpdate(camera, unused, seconds, flags);
        if (g_addedTo != nullptr) {
            Field<float>(g_addedTo, kEyeRight) -= g_added.right;
            Field<float>(g_addedTo, kEyeUp) -= g_added.up;
            g_addedTo = nullptr;
        }
    }
}

bool WeaponOverhaul::Aim::Install(DriftFn drift) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_update || !g_updateCameraOffset || !g_equippedWeapon || !g_playerCamera) {
        api->Log("aim: the camera functions were not found in this build");
        return false;
    }

    g_drift = drift;
    if (!api->Hook(reinterpret_cast<void*>(g_update.address()),
                   reinterpret_cast<void*>(&UpdateDetour),
                   reinterpret_cast<void**>(&g_originalUpdate))) {
        return false;
    }
    // A plugin refused past its first hook is unloaded with that hook live, so this only logs.
    if (!api->Hook(reinterpret_cast<void*>(g_updateCameraOffset.address()),
                   reinterpret_cast<void*>(&UpdateCameraOffsetDetour),
                   reinterpret_cast<void**>(&g_originalUpdateCameraOffset))) {
        api->Log("aim: the camera offset cannot be hooked, so nothing follows the sights");
    }
    return true;
}

float WeaponOverhaul::Aim::Settled() {
    return g_settled;
}

float WeaponOverhaul::Aim::Scoped() {
    return g_scoped;
}

WeaponOverhaul::Aim::Turn WeaponOverhaul::Aim::Turning() {
    return {g_turnRight, g_turnUp};
}
