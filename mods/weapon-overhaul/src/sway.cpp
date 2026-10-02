// The hand's wiggle down the sights: the eye drifting a few millimetres off the gun.
#include "sway.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <numbers>
#include <span>

namespace {
    // Metres.
    constexpr float kDrift = 0.002f;
    // Seconds to settle into the sights, and to let go of them.
    constexpr float kSettle = 0.4f;

    struct Wave {
        double hertz;
        double phase;
        float weight;
    };
    // Unrelated frequencies, so the drift never visibly repeats. The first upward wave is the breath.
    constexpr Wave kRight[] = {{0.185, 0.0, 0.55f}, {0.355, 1.3, 0.3f}, {0.645, 2.1, 0.15f}};
    constexpr Wave kUp[] = {{0.125, 0.0, 0.5f}, {0.215, 0.7, 0.55f}, {0.415, 2.6, 0.3f}, {0.835, 4.0, 0.15f}};

    std::atomic<bool> g_enabled{true};
    double g_time = 0.0;
    float g_settled = 0.0f;

    float Drift(std::span<const Wave> waves) {
        float sum = 0.0f;
        for (const Wave& wave : waves) {
            sum += wave.weight *
                   static_cast<float>(std::sin(2.0 * std::numbers::pi * wave.hertz * g_time + wave.phase));
        }
        return sum;
    }
}

WeaponOverhaul::Aim::Offset WeaponOverhaul::Sway::Eye(const Aim::Frame& frame) {
    g_time += frame.seconds;

    if (frame.scope) {
        g_settled = 0.0f;
        return {};
    }

    const float step = frame.seconds / kSettle;
    g_settled = std::clamp(g_settled + (frame.sights && g_enabled ? step : -step), 0.0f, 1.0f);
    if (g_settled == 0.0f) {
        return {};
    }

    const float ease = g_settled * g_settled * (3.0f - 2.0f * g_settled);
    return {ease * kDrift * Drift(kRight), ease * kDrift * Drift(kUp)};
}

void WeaponOverhaul::Sway::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
