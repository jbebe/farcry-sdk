// The hand's drift: a few millimetres of wander and a slow breath.
#include "sway.h"

#include <atomic>
#include <cmath>
#include <numbers>
#include <span>

namespace {
    // Metres.
    constexpr float kDrift = 0.002f;

    struct Wave {
        double hertz;
        double phase;
        float weight;
    };
    // Unrelated frequencies, so the drift never visibly repeats. The first upward wave is the breath.
    constexpr Wave kRight[] = {{0.185, 0.0, 0.55f}, {0.355, 1.3, 0.3f}, {0.645, 2.1, 0.15f}};
    constexpr Wave kUp[] = {{0.125, 0.0, 0.5f}, {0.215, 0.7, 0.55f}, {0.415, 2.6, 0.3f},
                            {0.835, 4.0, 0.15f}};

    std::atomic<bool> g_enabled{true};
    double g_time = 0.0;

    float Sum(std::span<const Wave> waves) {
        float sum = 0.0f;
        for (const Wave& wave : waves) {
            sum += wave.weight * static_cast<float>(std::sin(
                                     2.0 * std::numbers::pi * wave.hertz * g_time + wave.phase));
        }
        return sum;
    }
}

WeaponOverhaul::Aim::Offset WeaponOverhaul::Sway::Drift(float seconds) {
    g_time += seconds;
    if (!g_enabled) {
        return {};
    }
    return {kDrift * Sum(kRight), kDrift * Sum(kUp)};
}

void WeaponOverhaul::Sway::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
