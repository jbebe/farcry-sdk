// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
//
// See docs/docs/engine-internals/first-person-aiming.md.
#include "engine/aim.h"

#include "engine/entity_name.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <numbers>
#include <string_view>

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
    constexpr ptrdiff_t kEyeAhead = 0xEC;
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
    // Also in it, two eased fields of view, each with how far the view has gone to it: another's,
    // and the iron sights', which the weapon's transition curve takes from nought to one.
    constexpr ptrdiff_t kIronsightWeight = 0xF0;
    constexpr ptrdiff_t kIronsightFov = 0xF4;
    constexpr ptrdiff_t kOtherWeight = 0x10C;
    constexpr ptrdiff_t kOtherFov = 0x110;

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

    AimingOverhaul::Aim::DriftFn g_drift = nullptr;
    AimingOverhaul::Aim::RaiseReachFn g_raiseReachOf = nullptr;
    std::atomic<bool> g_zoomAtOnce{true};
    float g_sights = 0.0f;
    float g_scope = 0.0f;
    std::atomic<float> g_settled{0.0f};
    std::atomic<float> g_scoped{0.0f};
    std::atomic<bool> g_scopeUp{false};
    std::atomic<bool> g_aiming{false};

    // After a shot the scope kicks back over kKickRise seconds and settles over about three
    // kKickSettle.
    constexpr float kKickRise = 0.03f;
    constexpr float kKickSettle = 0.06f;
    float g_kick = 0.0f;
    bool g_kicking = false;
    std::atomic<float> g_scopeKick{0.0f};
    std::atomic<float> g_magnification{1.0f};

    // The equipped weapon as last seen, its name, how many times it has changed, how far the eye
    // comes forward as its scope is raised, if the plugin draws that, and the iron sights' weight
    // the raise began at.
    uint8_t* g_weapon = nullptr;
    char g_weaponName[64] = {};
    std::atomic<uint32_t> g_weaponChanges{0};
    float g_raiseReach = 0.0f;
    float g_raiseFrom = 0.0f;

    // What was added to the eye during the camera update under way, and to which camera.
    uint8_t* g_addedTo = nullptr;
    AimingOverhaul::Aim::Offset g_added = {};
    float g_addedAhead = 0.0f;

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

    // Notes the equipped weapon's name, and its scope's raise, each time it changes.
    void FollowWeapon(uint8_t* weapon) {
        if (weapon == g_weapon) {
            return;
        }
        g_weapon = weapon;
        AimingOverhaul::EntityName::Read(weapon, g_weaponName);
        g_raiseReach = g_raiseReachOf(g_weaponName);
        g_weaponChanges++;
    }

    // Whether the weapon has a scope's sight picture of its own, and whether that is up.
    bool HasScope(uint8_t* weapon) {
        return weapon != nullptr && Field<uint8_t>(weapon, kHiResScope) != 0;
    }

    bool SightPictureUp(uint8_t* weapon) {
        return HasScope(weapon) && Field<uint8_t>(weapon, kScopeShown) != 0;
    }

    // Whether the event is the one named, read from the std::string at its +0x08, whose characters
    // are at +0x0C, inline below a capacity of 16. False where the engine's pointers do not hold.
    bool EventIs(uint8_t* event, std::string_view name) {
        __try {
            const uint32_t length = Field<uint32_t>(event, 0x1C);
            const uint32_t capacity = Field<uint32_t>(event, 0x20);
            const char* text = capacity < 16 ? reinterpret_cast<const char*>(event + 0x0C)
                                             : Field<const char*>(event, 0x0C);
            return length == name.size() && std::memcmp(text, name.data(), length) == 0;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return false;
        }
    }

    // How far a scope has kicked back from the last shot, from nought to one: a quick rise, then an
    // eased return, as a stock comes back onto the shoulder. Another shot rises from where it is.
    void FollowShots(bool scope, float seconds) {
        if (!scope) {
            g_kick = 0.0f;
            g_kicking = false;
        } else if (g_kicking) {
            g_kick = (std::min)(g_kick + seconds / kKickRise, 1.0f);
            g_kicking = g_kick < 1.0f;
        } else {
            g_kick *= std::exp(-seconds / kKickSettle);
        }
        g_scopeKick = g_kick;
    }

    // The engine tells the weapon in hand of each round it fires.
    using WeaponEventFn = bool(__fastcall*)(uint8_t* weapon, void* unused, uint8_t* event);
    FCSE::Relocation<WeaponEventFn> g_weaponEvent{FCSE::Uplay(0x006D3D00)};
    WeaponEventFn g_originalWeaponEvent = nullptr;

    bool __fastcall WeaponEventDetour(uint8_t* weapon, void* unused, uint8_t* event) {
        if (weapon == g_weapon && EventIs(event, "WeaponFired")) {
            g_kicking = true;
        }
        return g_originalWeaponEvent(weapon, unused, event);
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
        uint8_t* weapon = g_equippedWeapon(data + kInventory);
        FollowWeapon(weapon);
        // The scope's sight picture outlasts the iron sights by a few frames as the scope goes.
        const bool scope = SightPictureUp(weapon);
        const float step = seconds / kSettle;
        // The iron sights let go at once when a scope's own sight picture comes up, and the scope
        // when it goes.
        g_sights = scope ? 0.0f : Approach(g_sights, sights, step);
        g_scope = scope ? Approach(g_scope, true, step) : 0.0f;
        const float settled = Ease(g_sights);
        const float scoped = Ease(g_scope);
        g_settled = settled;
        g_scoped = scoped;
        FollowShots(scope, seconds);
        g_scopeUp = scope;
        g_aiming = settled > 0.0f || scope;
        FollowLook(pawn, data, seconds);

        // Down the iron sights the eye drifts off the gun. To a scope it comes forward only while the
        // scope is raised: the eyepiece hides the gun once it is up, where a shot's recoil would
        // bring the gun too near the eye for the engine to draw, and the gun is lowered as the game
        // lowers it.
        const float weight = std::clamp(Field<float>(data, kIronsightWeight), 0.0f, 1.0f);
        const bool raising = HasScope(weapon) && g_zoomAtOnce && sights && !scope;
        if (!raising) {
            g_raiseFrom = weight;
        }
        const float raised =
            raising && g_raiseFrom < 1.0f ? (weight - g_raiseFrom) / (1.0f - g_raiseFrom) : 0.0f;
        const AimingOverhaul::Aim::Offset drift = g_drift(seconds);
        g_added = {drift.right * settled, drift.up * settled};
        g_addedAhead = g_raiseReach * raised;
        Field<float>(camera, kEyeRight) += g_added.right;
        Field<float>(camera, kEyeUp) += g_added.up;
        Field<float>(camera, kEyeAhead) += g_addedAhead;
        g_addedTo = camera;
    }

    // The field of view worked out again with a scope's magnification wholly in while its sight
    // picture is up and wholly out while it is not, so that it never eases in over the raise.
    void __fastcall UpdateFovDetour(uint8_t* camera, void* unused, uint8_t* pawn, float seconds) {
        g_originalUpdateFov(camera, unused, pawn, seconds);
        if (camera != g_playerCamera()) {
            return;
        }
        uint8_t* data = Field<uint8_t*>(pawn, kPawnData);
        uint8_t* weapon = g_equippedWeapon(data + kInventory);
        if (!HasScope(weapon) || Field<float>(data, kIronsightWeight) <= 0.0f) {
            return;
        }
        const float base = Field<float>(camera, kBaseFov);
        const float scoped = Field<float>(data, kIronsightFov);
        g_magnification = std::tan(0.5f * base) / std::tan(0.5f * scoped);
        if (!g_zoomAtOnce) {
            return;
        }
        const float zoom = SightPictureUp(weapon) ? 1.0f : 0.0f;
        float fov = base;
        fov += (Field<float>(data, kOtherFov) - fov) * Field<float>(data, kOtherWeight);
        fov += (scoped - fov) * zoom;
        Field<float>(camera, kFov) = fov + Field<float>(camera, kFovNoise);
    }

    void __fastcall UpdateDetour(uint8_t* camera, void* unused, float seconds, uint32_t flags) {
        g_originalUpdate(camera, unused, seconds, flags);
        if (g_addedTo != nullptr) {
            Field<float>(g_addedTo, kEyeRight) -= g_added.right;
            Field<float>(g_addedTo, kEyeUp) -= g_added.up;
            Field<float>(g_addedTo, kEyeAhead) -= g_addedAhead;
            g_addedTo = nullptr;
        }
    }
}

bool AimingOverhaul::Aim::Install(DriftFn drift, RaiseReachFn raiseReach) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_update || !g_updateCameraOffset || !g_equippedWeapon || !g_playerCamera) {
        api->Log("aim: the camera functions were not found in this build");
        return false;
    }

    g_drift = drift;
    g_raiseReachOf = raiseReach;
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
    if (!g_weaponEvent || !api->Hook(reinterpret_cast<void*>(g_weaponEvent.address()),
                                     reinterpret_cast<void*>(&WeaponEventDetour),
                                     reinterpret_cast<void**>(&g_originalWeaponEvent))) {
        api->Log("aim: the weapon's events cannot be hooked, so a scope does not kick back");
    }
    return true;
}

void AimingOverhaul::Aim::SetZoomAtOnce(bool atOnce) {
    g_zoomAtOnce = atOnce;
}

const char* AimingOverhaul::Aim::WeaponName() {
    return g_weaponName;
}

uint32_t AimingOverhaul::Aim::WeaponChanges() {
    return g_weaponChanges;
}

float AimingOverhaul::Aim::Settled() {
    return g_settled;
}

float AimingOverhaul::Aim::Scoped() {
    return g_scoped;
}

bool AimingOverhaul::Aim::ScopeUp() {
    return g_scopeUp;
}

bool AimingOverhaul::Aim::Aiming() {
    return g_aiming;
}

AimingOverhaul::Aim::Swing AimingOverhaul::Aim::ScopeSwing() {
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

float AimingOverhaul::Aim::Magnification() {
    return g_magnification;
}

float AimingOverhaul::Aim::ScopeKick() {
    return g_scopeKick;
}
