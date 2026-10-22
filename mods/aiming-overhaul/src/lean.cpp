// Leaning: held Q or E rolls the view to the side, the arms and the gun with it. The eye does not
// move out yet; see docs/docs/engine-internals/first-person-aiming.md#leaning.
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

    // Radians at full lean, and seconds for the roll to follow, which takes it most of the way in a
    // quarter of a second.
    constexpr float kMostRoll = 0.21f;
    constexpr float kFollow = 0.08f;

    std::atomic<bool> g_enabled{true};
    bool g_left = false;
    bool g_right = false;
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

float AimingOverhaul::Lean::Roll(float seconds, bool free) {
    float target = 0.0f;
    if (g_enabled && free) {
        target = kMostRoll * ((g_right ? 1.0f : 0.0f) - (g_left ? 1.0f : 0.0f));
    }
    g_roll = Follow(g_roll, target, seconds, kFollow);
    if (target == 0.0f && std::fabs(g_roll) < 1e-4f) {
        g_roll = 0.0f;
    }
    return g_roll;
}

void AimingOverhaul::Lean::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
