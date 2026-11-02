// The gun near walls: a gun that would go into what is ahead swings down out of it, and while it is
// down it neither fires nor comes up to the sights.
#include "wall.h"

#include "ease.h"
#include "engine/ray.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <numbers>

namespace {
    // Metres ahead of the eye a gun reaches, and how much less room swings it from up to all the
    // way down.
    constexpr float kReach = 0.8f;
    constexpr float kSwingRoom = 0.5f;
    // Radians all the way down, and seconds to swing all the way down, and back up.
    constexpr float kMostSwing = 0.6f;
    constexpr float kSwingTime = 0.2f;
    constexpr float kRaiseTime = 0.3f;
    // The gun comes back up only once there is kClearRoom metres more room than it swung down for,
    // and has been for kClearHold seconds, so what sways in and out of reach does not shake it.
    constexpr float kClearRoom = 0.1f;
    constexpr float kClearHold = 0.4f;
    // The share of the swing past which the gun is blocked, and below which it is free again.
    constexpr float kBlockAbove = 0.9f;
    constexpr float kFreeBelow = 0.7f;

    std::atomic<bool> g_enabled{true};
    float g_swing = 0.0f;
    float g_clear = 0.0f;
    bool g_blocked = false;

    // How far the gun is swung down, in radians.
    float Down() {
        return kMostSwing * AimingOverhaul::Ease(g_swing);
    }
}

void AimingOverhaul::Wall::Update(float seconds, const float eye[3], const float forward[3]) {
    float target = 0.0f;
    if (g_enabled && eye != nullptr) {
        const float room = Ray::Cast(eye, forward, kReach);
        target = std::clamp((kReach - room) / kSwingRoom, 0.0f, 1.0f);
    }    if (target >= g_swing) {
        g_swing = Approach(g_swing, target, seconds / kSwingTime);
        g_clear = 0.0f;
    } else if (g_clear >= kClearHold || target < g_swing - kClearRoom / kSwingRoom) {
        g_clear += seconds;
        if (g_clear >= kClearHold) {
            g_swing = Approach(g_swing, target, seconds / kRaiseTime);
        }
    } else {
        g_clear = 0.0f;
    }
    if (g_swing > kBlockAbove) {
        g_blocked = true;
    } else if (g_swing < kFreeBelow) {
        g_blocked = false;
    }
}

AimingOverhaul::Body::Offset AimingOverhaul::Wall::Posed() {
    return {0.0f, -Down(), 0.0f, 0.0f, 0.0f, 0.0f};
}

void AimingOverhaul::Wall::Bend(float direction[3]) {
    const float down = Down();
    const float level = std::hypot(direction[0], direction[1]);
    if (down <= 0.0f || level <= 0.0f) {
        return;
    }
    const float pitch = (std::max)(std::atan2(direction[2], level) - down,
                                   -0.5f * std::numbers::pi_v<float>);
    const float scale = std::cos(pitch) / level;
    direction[0] *= scale;
    direction[1] *= scale;
    direction[2] = std::sin(pitch);
}

bool AimingOverhaul::Wall::Blocked() {
    return g_blocked;
}

void AimingOverhaul::Wall::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
