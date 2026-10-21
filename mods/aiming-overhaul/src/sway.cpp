// The hand's drift: a slow breath and a wander, which move the eye a few millimetres off the gun and
// the aim a fraction of a degree, less while the breath is held.
#include "sway.h"

#include "breath.h"

#include <atomic>
#include <cmath>
#include <numbers>
#include <span>

namespace {
    // Metres.
    constexpr float kDrift = 0.002f;
    // Radians, down the iron sights and through a scope.
    constexpr float kIronsightWander = 0.0035f;
    constexpr float kScopeWander = 0.006f;

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

AimingOverhaul::Aim::Offset AimingOverhaul::Sway::Drift(float seconds) {
    g_time += seconds;
    if (!g_enabled) {
        return {};
    }
    const float drift = kDrift * Breath::Steadiness();
    return {drift * Sum(kRight), drift * Sum(kUp)};
}

AimingOverhaul::Look::Angles AimingOverhaul::Sway::Wander() {
    if (!g_enabled) {
        return {};
    }
    const float wander = (kIronsightWander * Aim::Settled() + kScopeWander * Aim::Scoped()) *
                         Breath::Steadiness();
    return {wander * Sum(kUp), wander * Sum(kRight)};
}

void AimingOverhaul::Sway::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
