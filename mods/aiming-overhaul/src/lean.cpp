// Leaning: held Q or E rolls the body out to the side, eye, gun and all.
#include "lean.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    constexpr uint32_t kStartLeft = FCSE::Crc32("start_lean_left");
    constexpr uint32_t kStopLeft = FCSE::Crc32("stop_lean_left");
    constexpr uint32_t kStartRight = FCSE::Crc32("start_lean_right");
    constexpr uint32_t kStopRight = FCSE::Crc32("stop_lean_right");

    // Radians at full lean, which takes the eye about 0.33 m to the side, and seconds to get there.
    constexpr float kMostRoll = 0.21f;
    constexpr float kLeanTime = 0.25f;

    std::atomic<bool> g_enabled{true};
    bool g_left = false;
    bool g_right = false;
    // From -1, full left, to 1, full right.
    float g_lean = 0.0f;

    float Ease(float linear) {
        const float a = std::fabs(linear);
        return (linear < 0.0f ? -1.0f : 1.0f) * a * a * (3.0f - 2.0f * a);
    }
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

float AimingOverhaul::Lean::Roll(float seconds, bool free) {
    const float target = g_enabled && free ? (g_right ? 1.0f : 0.0f) - (g_left ? 1.0f : 0.0f) : 0.0f;
    const float step = seconds / kLeanTime;
    g_lean = target > g_lean ? (std::min)(g_lean + step, target) : (std::max)(g_lean - step, target);
    return kMostRoll * Ease(g_lean);
}

void AimingOverhaul::Lean::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
