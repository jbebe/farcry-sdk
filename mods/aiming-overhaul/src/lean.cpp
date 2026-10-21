// Leaning: held Q or E rolls the body out to the side, eye, gun and all.
#include "lean.h"

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

    std::atomic<bool> g_enabled{true};
    bool g_left = false;
    bool g_right = false;
    float g_auto = 0.0f;
    bool g_free = false;
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

void AimingOverhaul::Lean::SetAuto(float roll) {
    g_auto = roll;
}

float AimingOverhaul::Lean::Roll(float seconds, bool free) {
    g_free = free;
    const float target = Target();
    g_roll += (target - g_roll) * (1.0f - std::exp(-seconds / kFollow));
    if (target == 0.0f && std::fabs(g_roll) < 1e-4f) {
        g_roll = 0.0f;
    }
    return g_roll;
}

float AimingOverhaul::Lean::Current() {
    return g_roll;
}

float AimingOverhaul::Lean::Target() {
    if (!g_enabled || !g_free) {
        return 0.0f;
    }
    if (g_left || g_right) {
        return kMostRoll * ((g_right ? 1.0f : 0.0f) - (g_left ? 1.0f : 0.0f));
    }
    return g_auto;
}

void AimingOverhaul::Lean::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
