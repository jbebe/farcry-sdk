// The player's body, rolled about its feet: the eye, the arms and the gun, and where a shot leaves
// from, all follow the pawn entity's orientation.
//
// See docs/docs/engine-internals/first-person-aiming.md#leaning.
#include "engine/body.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstddef>
#include <numbers>

namespace {
    using AimingOverhaul::Field;

    // CEntity: finishing its pending job before it is touched, and SetEuler(pitch, roll, yaw),
    // which rebuilds its basis as yaw about Z, after roll about its forward Y, after pitch about X.
    using FlushJobFn = void(__fastcall*)(uint8_t* entity);
    using SetEulerFn = void(__fastcall*)(uint8_t* entity, void* unused, float pitch, float roll,
                                         float yaw);
    FCSE::Relocation<FlushJobFn> g_flushJob{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<SetEulerFn> g_setEuler{FCSE::Uplay(0x004E01C0)};

    // A pawn's entity proxy and the entity in it; the entity's 4x4 world transform, rows the basis.
    constexpr ptrdiff_t kPawnProxy = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;
    constexpr ptrdiff_t kMatrix = 0x30;
    // A forward axis tilted further than this off level is a body the engine pitched; it is left be.
    constexpr float kMostTilt = 0.02f;

    bool g_installed = false;
    bool g_rolled = false;
}

bool AimingOverhaul::Body::Install() {
    g_installed = g_flushJob && g_setEuler;
    if (!g_installed) {
        FCSE::ApiPointer()->Log("body: the calls that turn an entity were not found in this build");
    }
    return g_installed;
}

void AimingOverhaul::Body::Roll(uint8_t* pawn, float radians) {
    if (!g_installed || (radians == 0.0f && !g_rolled)) {
        return;
    }
    uint8_t* proxy = Field<uint8_t*>(pawn, kPawnProxy);
    uint8_t* entity = proxy != nullptr ? Field<uint8_t*>(proxy, kProxyEntity) : nullptr;
    if (entity == nullptr) {
        return;
    }
    g_flushJob(entity);
    const float* m = &Field<float>(entity, kMatrix);
    if (std::fabs(m[6]) > kMostTilt) {
        return;
    }
    const float yaw = std::atan2(m[5], m[4]) - 0.5f * std::numbers::pi_v<float>;
    g_setEuler(entity, nullptr, 0.0f, radians, yaw);
    g_rolled = radians != 0.0f;
}
