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
    using UpdateFovFn = void(__fastcall*)(uint8_t* camera, void* unused, uint8_t* pawn,
                                          float seconds);
    using EquippedWeaponFn = uint8_t*(__fastcall*)(uint8_t* inventory);
    using PlayerCameraFn = uint8_t*(__cdecl*)();

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x00693C00)};
    FCSE::Relocation<UpdateCameraOffsetFn> g_updateCameraOffset{FCSE::Uplay(0x00693490)};
    FCSE::Relocation<UpdateFovFn> g_updateFov{FCSE::Uplay(0x00692E30)};
    FCSE::Relocation<EquippedWeaponFn> g_equippedWeapon{FCSE::Uplay(0x00127DA0)};
    FCSE::Relocation<PlayerCameraFn> g_playerCamera{FCSE::Uplay(0x0070C210)};

    UpdateFn g_originalUpdate = nullptr;
    UpdateCameraOffsetFn g_originalUpdateCameraOffset = nullptr;
    UpdateFovFn g_originalUpdateFov = nullptr;

    // CCameraPawnComponent: the eye's offset in the view's axes, x right, y ahead, z up; and the
    // field of view in radians, unaimed, as this frame's, and the noise added to it.
    constexpr ptrdiff_t kEyeRight = 0xE8;
    constexpr ptrdiff_t kEyeUp = 0xF0;
    constexpr ptrdiff_t kBaseFov = 0x70;
    constexpr ptrdiff_t kFov = 0x108;
    constexpr ptrdiff_t kFovNoise = 0x11C;

    // CPawn's data, and in it the effective flags, the effective look in radians (pitch up, yaw
    // left), and the inventory.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kEffectiveFlags = 0x2D4;
    constexpr uint8_t kIronsight = 0x08;
    constexpr ptrdiff_t kEffectivePitch = 0x308;
    constexpr ptrdiff_t kEffectiveYaw = 0x310;
    constexpr ptrdiff_t kInventory = 0x4F0;
    // Also in it, the field of view an override takes the view to and how far, and the iron
    // sights' and how far, which the weapon's transition curve takes from nought to one.
    constexpr ptrdiff_t kOverrideWeight = 0xF0;
    constexpr ptrdiff_t kOverrideFov = 0xF4;
    constexpr ptrdiff_t kIronsightWeight = 0x10C;
    constexpr ptrdiff_t kIronsightFov = 0x110;

    // CFCXWeapon: the scope picture as last asked for, and whether the weapon has one.
    constexpr ptrdiff_t kScopeShown = 0x84;
    constexpr ptrdiff_t kHiResScope = 0x85;

    // Seconds to settle into the sights, and to let go of them.
    constexpr float kSettle = 0.4f;
    // Seconds for the turn rate to follow the look. Faster than this in a frame is a cut, not a
    // turn.
    constexpr float kTrail = 0.18f;
    constexpr float kFastestTurn = 10.0f;

    // The scope's far end swings against the turn. Turns slower than kSlowTurn radians a second
    // hardly move it; past that it moves kSwingPerTurn opening radii per radian a second, and
    // kMostSwing at most.
    constexpr float kSlowTurn = 0.1f;
    constexpr float kSwingPerTurn = 1.5f;
    constexpr float kMostSwing = 0.5f;

    WeaponOverhaul::Aim::DriftFn g_drift = nullptr;
    std::atomic<bool> g_zoomAtOnce{true};
    float g_sights = 0.0f;
    float g_scope = 0.0f;
    std::atomic<float> g_settled{0.0f};
    std::atomic<float> g_scoped{0.0f};
    std::atomic<bool> g_scopeUp{false};
    std::atomic<uint32_t> g_scopeUps{0};

    // What was added to the eye during the camera update under way, and to which camera.
    uint8_t* g_addedTo = nullptr;
    WeaponOverhaul::Aim::Offset g_added = {};

    // The look last frame, for which pawn, and how fast it has been turning, in radians a second.
    uint8_t* g_lookPawn = nullptr;
    float g_lastPitch = 0.0f;
    float g_lastYaw = 0.0f;
    float g_turnRight = 0.0f;
    float g_turnUp = 0.0f;
    std::atomic<float> g_turnedRight{0.0f};
    std::atomic<float> g_turnedUp{0.0f};

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

    // How fast the look turns, smoothed over kTrail seconds.
    void FollowLook(uint8_t* pawn, uint8_t* data, float seconds) {
        const float pitch = Field<float>(data, kEffectivePitch);
        const float yaw = Field<float>(data, kEffectiveYaw);
        if (pawn != g_lookPawn || seconds <= 0.0f) {
            g_lookPawn = pawn;
            g_lastPitch = pitch;
            g_lastYaw = yaw;
            return;
        }
        // Yaw is left; it wraps at a turn.
        const float yawStep = std::remainder(yaw - g_lastYaw, 2.0f * std::numbers::pi_v<float>);
        const float right = std::clamp(-yawStep / seconds, -kFastestTurn, kFastestTurn);
        const float up = std::clamp((pitch - g_lastPitch) / seconds, -kFastestTurn, kFastestTurn);
        g_lastPitch = pitch;
        g_lastYaw = yaw;

        const float follow = 1.0f - std::exp(-seconds / kTrail);
        g_turnRight += (right - g_turnRight) * follow;
        g_turnUp += (up - g_turnUp) * follow;
        g_turnedRight = g_turnRight;
        g_turnedUp = g_turnUp;
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
        const float scoped = Ease(g_scope);
        g_settled = settled;
        g_scoped = scoped;
        if (scope && !g_scopeUp) {
            g_scopeUps++;
        }
        g_scopeUp = scope;
        FollowLook(pawn, data, seconds);

        // Down the iron sights the eye drifts off the gun.
        const WeaponOverhaul::Aim::Offset drift = g_drift(seconds);
        g_added = {drift.right * settled, drift.up * settled};
        Field<float>(camera, kEyeRight) += g_added.right;
        Field<float>(camera, kEyeUp) += g_added.up;
        g_addedTo = camera;
    }

    // The field of view worked out again with a scope's magnification wholly in while its sight
    // picture is up and wholly out while it is not, so that it never eases in over the raise.
    void __fastcall UpdateFovDetour(uint8_t* camera, void* unused, uint8_t* pawn, float seconds) {
        g_originalUpdateFov(camera, unused, pawn, seconds);
        if (!g_zoomAtOnce || camera != g_playerCamera()) {
            return;
        }
        uint8_t* data = Field<uint8_t*>(pawn, kPawnData);
        uint8_t* weapon = g_equippedWeapon(data + kInventory);
        if (weapon == nullptr || Field<uint8_t>(weapon, kHiResScope) == 0 ||
            Field<float>(data, kIronsightWeight) <= 0.0f) {
            return;
        }
        const float zoom = Field<uint8_t>(weapon, kScopeShown) != 0 ? 1.0f : 0.0f;
        const float base = Field<float>(camera, kBaseFov);
        float fov = base + (Field<float>(data, kIronsightFov) - base) * zoom;
        fov += (Field<float>(data, kOverrideFov) - fov) * Field<float>(data, kOverrideWeight);
        Field<float>(camera, kFov) = fov + Field<float>(camera, kFovNoise);
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
    if (!g_updateFov || !api->Hook(reinterpret_cast<void*>(g_updateFov.address()),
                                   reinterpret_cast<void*>(&UpdateFovDetour),
                                   reinterpret_cast<void**>(&g_originalUpdateFov))) {
        api->Log("aim: the field of view cannot be hooked, so a scope zooms in as it comes up");
    }
    return true;
}

void WeaponOverhaul::Aim::SetZoomAtOnce(bool atOnce) {
    g_zoomAtOnce = atOnce;
}

float WeaponOverhaul::Aim::Settled() {
    return g_settled;
}

float WeaponOverhaul::Aim::Scoped() {
    return g_scoped;
}

bool WeaponOverhaul::Aim::ScopeUp() {
    return g_scopeUp;
}

uint32_t WeaponOverhaul::Aim::ScopeUps() {
    return g_scopeUps;
}

WeaponOverhaul::Aim::Swing WeaponOverhaul::Aim::ScopeSwing() {
    const float right = g_turnedRight;
    const float up = g_turnedUp;
    const float speed = std::hypot(right, up);
    if (speed <= 0.0f) {
        return {};
    }
    const float eased = speed * speed / (speed + kSlowTurn);
    const float swing = kMostSwing * std::tanh(eased * kSwingPerTurn / kMostSwing) / speed;
    return {-right * swing, up * swing};
}
