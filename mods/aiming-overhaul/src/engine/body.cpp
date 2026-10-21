// The player's body, leaned about the belly once the physics has placed it each frame, so the arms,
// the gun, the eye and where a shot leaves from are all posed from the leaned body.
//
// See docs/docs/engine-internals/first-person-aiming.md#leaning.
#include "engine/body.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <numbers>

namespace {
    using AimingOverhaul::Field;

    // The physics post-step queue's update, __thiscall(seconds): each registered entity's physics
    // component places its entity, after the physics step and before the post-physics tick list.
    using PostStepFn = void(__fastcall*)(void* queue, void* unused, float seconds);
    FCSE::Relocation<PostStepFn> g_postStep{FCSE::Uplay(0x0058D8F0)};
    PostStepFn g_originalPostStep = nullptr;

    // CEntity: finishing its pending job before it is touched; SetPosition; and SetEuler(pitch,
    // roll, yaw), which rebuilds its basis as yaw about Z after roll about its forward Y.
    using FlushJobFn = void(__fastcall*)(uint8_t* entity);
    using Vec3Fn = void(__fastcall*)(uint8_t* entity, void* unused, float a, float b, float c);
    FCSE::Relocation<FlushJobFn> g_flushJob{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<Vec3Fn> g_setPosition{FCSE::Uplay(0x004E01B0)};
    FCSE::Relocation<Vec3Fn> g_setEuler{FCSE::Uplay(0x004E01C0)};

    // A pawn's entity proxy: its reference count and the entity, null once the entity is gone; at
    // a count of nought it is released, __thiscall, then freed.
    using ReleaseRefFn = void(__fastcall*)(uint8_t* proxy);
    using FreeFn = void(__cdecl*)(uint8_t* proxy);
    FCSE::Relocation<ReleaseRefFn> g_releaseRef{FCSE::Uplay(0x0029B7F0)};
    FCSE::Relocation<FreeFn> g_free{FCSE::Uplay(0x00228AD0)};
    constexpr ptrdiff_t kPawnProxy = 0x08;
    constexpr ptrdiff_t kRefCount = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;

    // The entity's 4x4 world transform, rows the basis then the position. A forward axis tilted
    // further than kMostTilt off level is a body the engine pitched, which is left be.
    constexpr ptrdiff_t kMatrix = 0x30;
    constexpr float kMostTilt = 0.02f;

    // Post-steps without a Lean after which the body stands upright.
    constexpr int kStale = 4;

    uint8_t* g_proxy = nullptr;
    float g_roll = 0.0f;
    int g_sinceLean = kStale;
    // Whether the body stands leaned, the position it was leaned from, and where it was put.
    bool g_leaned = false;
    float g_base[3] = {};
    float g_put[3] = {};

    void Release() {
        if (g_proxy != nullptr && --Field<int32_t>(g_proxy, kRefCount) == 0) {
            g_releaseRef(g_proxy);
            g_free(g_proxy);
        }
        g_proxy = nullptr;
        g_leaned = false;
    }

    void Hold(uint8_t* proxy) {
        if (proxy == g_proxy) {
            return;
        }
        Release();
        g_proxy = proxy;
        if (proxy != nullptr) {
            ++Field<int32_t>(proxy, kRefCount);
        }
    }

    // The body rolled about the belly from where the physics put it. Where the physics left it as
    // it was put last frame, it is leaned from where it was leaned from then.
    void Pose() {
        const bool stale = ++g_sinceLean > kStale;
        const float roll = stale ? 0.0f : g_roll;
        uint8_t* entity = g_proxy != nullptr ? Field<uint8_t*>(g_proxy, kProxyEntity) : nullptr;
        if (entity == nullptr || (roll == 0.0f && !g_leaned)) {
            if (stale || entity == nullptr) {
                Release();
            }
            return;
        }
        g_flushJob(entity);
        const float* m = &Field<float>(entity, kMatrix);
        if (std::fabs(m[6]) > kMostTilt) {
            return;
        }
        const float* position = m + 12;
        const bool kept = g_leaned && position[0] == g_put[0] && position[1] == g_put[1] &&
                          position[2] == g_put[2];
        if (!kept) {
            std::copy(position, position + 3, g_base);
        }
        const float yaw = std::atan2(m[5], m[4]) - 0.5f * std::numbers::pi_v<float>;
        const float side = -AimingOverhaul::Body::kPivotHeight * std::sin(roll);
        const float rise = AimingOverhaul::Body::kPivotHeight * (1.0f - std::cos(roll));
        g_setPosition(entity, nullptr, g_base[0] + side * std::cos(yaw),
                      g_base[1] + side * std::sin(yaw), g_base[2] + rise);
        g_setEuler(entity, nullptr, 0.0f, roll, yaw);
        std::copy(position, position + 3, g_put);
        g_leaned = roll != 0.0f;
    }

    void __fastcall PostStepDetour(void* queue, void* unused, float seconds) {
        g_originalPostStep(queue, unused, seconds);
        Pose();
    }
}

bool AimingOverhaul::Body::Install() {
    if (!g_flushJob || !g_setPosition || !g_setEuler || !g_releaseRef || !g_free ||
        !Hook(g_postStep.address(), &PostStepDetour, &g_originalPostStep)) {
        FCSE::ApiPointer()->Log("body: the physics post-step cannot be hooked, so the body does "
                                "not lean");
        return false;
    }
    return true;
}

void AimingOverhaul::Body::Lean(uint8_t* pawn, float roll) {
    Hold(pawn != nullptr ? Field<uint8_t*>(pawn, kPawnProxy) : nullptr);
    g_roll = roll;
    g_sinceLean = 0;
}
