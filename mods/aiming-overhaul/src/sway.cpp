// The hand's drift: a breathing cycle that rests calm at the bottom of each breath, and a wander,
// which move the eye a few millimetres off the gun and the aim a fraction of a degree.
#include "sway.h"

#include "breath.h"
#include "ease.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <numbers>
#include <span>

namespace {
    // Metres.
    constexpr float kDrift = 0.002f;
    // Radians, down the iron sights and through a scope, and how many times that through the scope
    // of a gun whose breath can be held.
    constexpr float kIronsightWander = 0.0035f;
    constexpr float kScopeWander = 0.006f;
    constexpr float kHoldBreathScope = 2.0f;

    // The breath: seconds drawing in, letting out and resting at the bottom, how far it lifts the
    // aim against the wander's weights, the share of the wander left in the rest, and the seconds
    // the rest takes to settle into and out of.
    constexpr double kBreathIn = 1.5;
    constexpr double kBreathOut = 2.0;
    constexpr double kRest = 1.5;
    constexpr float kBreathDepth = 0.5f;
    constexpr float kRestCalm = 0.4f;
    constexpr double kRestSettle = 0.3;
    // Seconds for the aim to follow the breath, and to settle to the bottom as it is held: most of
    // the way in half a second.
    constexpr float kFollowBreath = 0.03f;
    constexpr float kSettle = 0.15f;

    struct Wave {
        double hertz;
        double phase;
        float weight;
    };
    // The wander: unrelated frequencies, so it never visibly repeats.
    constexpr Wave kRight[] = {{0.185, 0.0, 0.55f}, {0.355, 1.3, 0.3f}, {0.645, 2.1, 0.15f}};
    constexpr Wave kUp[] = {{0.215, 0.7, 0.55f}, {0.415, 2.6, 0.3f}, {0.835, 4.0, 0.15f}};

    std::atomic<bool> g_enabled{true};
    double g_time = 0.0;
    // Seconds into the breath; while it is held, settled into the rest, where it goes on from.
    double g_breathTime = 0.0;
    // How far the breath has lifted the aim, from nought to one.
    float g_lift = 0.0f;
    // The wander at g_time, rightward and upward, and where the breath has the aim, up from the
    // middle of its rise and fall.
    float g_right = 0.0f;
    float g_up = 0.0f;
    float g_breath = 0.0f;
    AimingOverhaul::Sway::Turn g_turned = AimingOverhaul::Sway::Turn::None;

    float Sum(std::span<const Wave> waves) {
        float sum = 0.0f;
        for (const Wave& wave : waves) {
            sum += wave.weight * static_cast<float>(std::sin(
                                     2.0 * std::numbers::pi * wave.hertz * g_time + wave.phase));
        }
        return sum;
    }

    // How far the breath lifts the aim at `t` seconds into it, from nought at the bottom to one at
    // the top.
    float Lift(double t) {
        if (t < kBreathIn) {
            return static_cast<float>(0.5 - 0.5 * std::cos(std::numbers::pi * t / kBreathIn));
        }
        if (t < kBreathIn + kBreathOut) {
            return static_cast<float>(
                0.5 + 0.5 * std::cos(std::numbers::pi * (t - kBreathIn) / kBreathOut));
        }
        return 0.0f;
    }

    // The wander and the breath upward together. Holding the breath steadies the wander, which the
    // breath's rest has already calmed; winded, the breath runs deeper.
    float Up() {
        const float steadiness = AimingOverhaul::Breath::Steadiness();
        return steadiness * g_up + (std::max)(steadiness, 1.0f) * g_breath;
    }

    // How much of the wander is left at `t` seconds into a breath: less in the rest at the bottom.
    float Calm(double t) {
        const double rest = t - kBreathIn - kBreathOut;
        if (rest < 0.0) {
            return 1.0f;
        }
        const double settled = (std::min)(1.0, (std::min)(rest, kRest - rest) / kRestSettle);
        return static_cast<float>(1.0 - (1.0 - kRestCalm) * settled);
    }
}

AimingOverhaul::Aim::Offset AimingOverhaul::Sway::Drift(float seconds) {
    g_time += seconds;
    // A held breath settles to the bottom and rests there, and goes on from the rest when let out.
    const bool held = Breath::Held();
    const double was = g_breathTime;
    if (held) {
        g_breathTime = kBreathIn + kBreathOut + kRestSettle;
    } else {
        g_breathTime = std::fmod(g_breathTime + seconds, kBreathIn + kBreathOut + kRest);
    }
    g_turned = held                                                ? Turn::None
               : g_breathTime < was                                ? Turn::In
               : was < kBreathIn && kBreathIn <= g_breathTime      ? Turn::Out
                                                                   : Turn::None;
    g_lift = Follow(g_lift, held ? 0.0f : Lift(g_breathTime), seconds,
                    held ? kSettle : kFollowBreath);
    if (!g_enabled) {
        return {};
    }
    const float calm = Calm(g_breathTime);
    g_right = calm * Sum(kRight);
    g_up = calm * Sum(kUp);
    g_breath = kBreathDepth * (2.0f * g_lift - 1.0f);
    return {kDrift * Breath::Steadiness() * g_right, kDrift * Up()};
}

AimingOverhaul::Look::Angles AimingOverhaul::Sway::Wander() {
    if (!g_enabled) {
        return {};
    }
    const float scope = kScopeWander * (Breath::Needed() ? kHoldBreathScope : 1.0f);
    const float wander = kIronsightWander * Aim::Settled() + scope * Aim::Scoped();
    return {wander * Up(), wander * Breath::Steadiness() * g_right};
}

AimingOverhaul::Sway::Turn AimingOverhaul::Sway::Turned() {
    return g_turned;
}

void AimingOverhaul::Sway::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
