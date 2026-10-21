// Leaning: held Q or E rolls the body out to the side about its feet, eye, gun and all.
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
    // Metres from the feet to the eye.
    constexpr float kEyeHeight = 1.6f;

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

void AimingOverhaul::Lean::Shift(const float forward[3], float from, float to, float out[3]) {
    const float level = std::hypot(forward[0], forward[1]);
    const float right[2] = {level > 0.0f ? forward[1] / level : 1.0f,
                            level > 0.0f ? -forward[0] / level : 0.0f};
    const float side = kEyeHeight * (std::sin(to) - std::sin(from));
    out[0] = right[0] * side;
    out[1] = right[1] * side;
    out[2] = kEyeHeight * (std::cos(to) - std::cos(from));
}

void AimingOverhaul::Lean::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
