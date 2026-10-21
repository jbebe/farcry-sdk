// Leaning: held Q or E rolls the upper body out to the side about the belly, so the eye swings
// sideways and a little down as the view rolls, and the gun goes with it.
#include "lean.h"

#include "ease.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>

namespace {
    constexpr uint32_t kStartLeft = FCSE::Crc32("start_lean_left");
    constexpr uint32_t kStopLeft = FCSE::Crc32("stop_lean_left");
    constexpr uint32_t kStartRight = FCSE::Crc32("start_lean_right");
    constexpr uint32_t kStopRight = FCSE::Crc32("stop_lean_right");

    // Seconds for the roll to follow, which takes it most of the way in a quarter of a second.
    constexpr float kFollow = 0.08f;
    // Metres from the belly up to the eye.
    constexpr float kPivot = 0.75f;

    std::atomic<bool> g_enabled{true};
    bool g_left = false;
    bool g_right = false;
    float g_target = 0.0f;
    float g_roll = 0.0f;
}

void AimingOverhaul::Lean::OnSignal(uint32_t signal) {
    if (signal == kStartLeft) {
        g_left = true;
    } else if (signal == kStopLeft) {
        g_left = false;
    } else if (signal == kStartRight) {
        g_right = true;
    } else if (signal == kStopRight) {
        g_right = false;
    }
}

float AimingOverhaul::Lean::Roll(float seconds, bool free, float autoRoll) {
    g_target = 0.0f;
    if (g_enabled && free) {
        g_target = g_left || g_right
                       ? kMostRoll * ((g_right ? 1.0f : 0.0f) - (g_left ? 1.0f : 0.0f))
                       : autoRoll;
    }
    g_roll = Follow(g_roll, g_target, seconds, kFollow);
    if (g_target == 0.0f && std::fabs(g_roll) < 1e-4f) {
        g_roll = 0.0f;
    }
    return g_roll;
}

float AimingOverhaul::Lean::Current() {
    return g_roll;
}

float AimingOverhaul::Lean::Target() {
    return g_target;
}

AimingOverhaul::Aim::Offset AimingOverhaul::Lean::Eye(float roll) {
    return {kPivot * std::sin(roll), kPivot * (std::cos(roll) - 1.0f)};
}

void AimingOverhaul::Lean::Shift(const float forward[3], float from, float to, float out[3]) {
    const float level = std::hypot(forward[0], forward[1]);
    const float right[3] = {level > 0.0f ? forward[1] / level : 1.0f,
                            level > 0.0f ? -forward[0] / level : 0.0f, 0.0f};
    // The view's up: right across forward.
    const float up[3] = {right[1] * forward[2], -right[0] * forward[2],
                         right[0] * forward[1] - right[1] * forward[0]};
    const Aim::Offset a = Eye(from);
    const Aim::Offset b = Eye(to);
    for (int i = 0; i < 3; i++) {
        out[i] = right[i] * (b.right - a.right) + up[i] * (b.up - a.up);
    }
}

void AimingOverhaul::Lean::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
