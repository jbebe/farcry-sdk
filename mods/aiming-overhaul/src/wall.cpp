// The gun near walls: a gun that would go into what is ahead swings down out of it, and while it is
// down it neither fires nor comes up to the sights.
#include "wall.h"

#include "engine/ray.h"

#include <algorithm>
#include <atomic>

namespace {
    // Metres ahead of the eye a gun reaches, and how much less room swings it from up to all the
    // way down.
    constexpr float kReach = 0.8f;
    constexpr float kSwingRoom = 0.35f;
    // Radians all the way down, and seconds to swing that far.
    constexpr float kMostSwing = 0.6f;
    constexpr float kSwingTime = 0.2f;
    // The share of the swing past which the gun is blocked, and below which it is free again.
    constexpr float kBlockAbove = 0.5f;
    constexpr float kFreeBelow = 0.3f;

    std::atomic<bool> g_enabled{true};
    float g_swing = 0.0f;
    std::atomic<bool> g_blocked{false};

    float Ease(float linear) {
        return linear * linear * (3.0f - 2.0f * linear);
    }
}

float AimingOverhaul::Wall::Update(float seconds, const float eye[3], const float forward[3]) {
    float target = 0.0f;
    if (g_enabled && eye != nullptr) {
        const float delta[3] = {forward[0] * kReach, forward[1] * kReach, forward[2] * kReach};
        const float room = Ray::Cast(eye, delta);
        target = std::clamp((kReach - room) / kSwingRoom, 0.0f, 1.0f);
    }
    const float step = seconds / kSwingTime;
    g_swing = target > g_swing ? (std::min)(g_swing + step, target)
                               : (std::max)(g_swing - step, target);
    if (g_swing > kBlockAbove) {
        g_blocked = true;
    } else if (g_swing < kFreeBelow) {
        g_blocked = false;
    }
    return kMostSwing * Ease(g_swing);
}

bool AimingOverhaul::Wall::Blocked() {
    return g_blocked;
}

void AimingOverhaul::Wall::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
