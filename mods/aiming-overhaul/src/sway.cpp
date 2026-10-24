// The hand's drift: a breathing cycle that rests calm at the bottom of each breath, a wander, and a
// jolt with each beat of the heart, which move the eye a few millimetres off the gun and the aim a
// fraction of a degree, all of it quicker and wider as the heart races.
#include "sway.h"

#include "breath.h"
#include "ease.h"
#include "heart.h"

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

    // The breath at rest: seconds drawing in, letting out and resting at the bottom, how far it
    // lifts the aim against the wander's weights, the share of the wander left in the rest, and the
    // seconds the rest takes to settle into and out of. A racing heart breathes in and out in up to
    // half the time, and rests for a quarter of it.
    constexpr double kBreathIn = 1.5;
    constexpr double kBreathOut = 2.0;
    constexpr double kRest = 1.5;
    constexpr double kRacingBreath = 0.5;
    constexpr float kBreathDepth = 1.0f;
    constexpr float kRestCalm = 0.4f;
    constexpr double kRestSettle = 0.3;
    // Seconds for the aim to follow the breath, and to settle to the bottom as it is held: most of
    // the way in half a second.
    constexpr float kFollowBreath = 0.03f;
    constexpr float kSettle = 0.15f;
    // How much wider the drift is with the heart at its fastest, and how far a beat jolts the aim up
    // against the wander's weights, at rest and at the fastest.
    constexpr float kRacingSway = 0.6f;
    constexpr float kRestingJolt = 0.08f;
    constexpr float kRacingJolt = 0.3f;

    struct Wave {
        double hertz;
        double phase;
        float weight;
    };
    // The wander: unrelated frequencies, so it never visibly repeats. Upward it is half the
    // breath's depth, so the rise and fall seen is the breath's.
    constexpr Wave kRight[] = {{0.185, 0.0, 0.55f}, {0.355, 1.3, 0.3f}, {0.645, 2.1, 0.15f}};
    constexpr Wave kUp[] = {{0.215, 0.7, 0.28f}, {0.415, 2.6, 0.15f}, {0.835, 4.0, 0.07f}};

    // A breath's seconds drawing in, letting out and resting.
    struct Pace {
        double in;
        double out;
        double rest;

        double Length() const {
            return in + out + rest;
        }
    };

    std::atomic<bool> g_enabled{true};
    double g_time = 0.0;
    // Seconds into the breath, and the breath as it was then; while it is held, settled into the
    // rest, where it goes on from.
    double g_breathTime = 0.0;
    Pace g_pace = {kBreathIn, kBreathOut, kRest};
    // How far the breath has lifted the aim, from nought to one.
    float g_lift = 0.0f;
    // The wander at g_time, rightward and upward, where the breath has the aim, up from the middle
    // of its rise and fall, and the beat's jolt up, all in the wander's weights.
    float g_right = 0.0f;
    float g_up = 0.0f;
    float g_breath = 0.0f;
    float g_jolt = 0.0f;
    AimingOverhaul::Sway::Turn g_turned = AimingOverhaul::Sway::Turn::None;

    float Sum(std::span<const Wave> waves) {
        float sum = 0.0f;
        for (const Wave& wave : waves) {
            sum += wave.weight * static_cast<float>(std::sin(
                                     2.0 * std::numbers::pi * wave.hertz * g_time + wave.phase));
        }
        return sum;
    }

    // The breath as the heart's racing paces it.
    Pace Paced(float racing) {
        const double quicker = 1.0 - kRacingBreath * racing;
        return {kBreathIn * quicker, kBreathOut * quicker, kRest * quicker * quicker};
    }

    // How far the breath lifts the aim at `t` seconds into it, from nought at the bottom to one at
    // the top.
    float Lift(double t, const Pace& b) {
        if (t < b.in) {
            return static_cast<float>(0.5 - 0.5 * std::cos(std::numbers::pi * t / b.in));
        }
        if (t < b.in + b.out) {
            return static_cast<float>(0.5 + 0.5 * std::cos(std::numbers::pi * (t - b.in) / b.out));
        }
        return 0.0f;
    }

    // How much of the wander is left at `t` seconds into a breath: less in the rest at the bottom.
    float Calm(double t, const Pace& b) {
        const double rest = t - b.in - b.out;
        if (rest < 0.0) {
            return 1.0f;
        }
        const double settled = (std::min)(1.0, (std::min)(rest, b.rest - rest) / kRestSettle);
        return static_cast<float>(1.0 - (1.0 - kRestCalm) * settled);
    }

    // The wander, the breath and the beat upward together. Holding the breath steadies the wander,
    // which the breath's rest has already calmed; winded, the breath runs deeper.
    float Up() {
        const float steadiness = AimingOverhaul::Breath::Steadiness();
        return steadiness * g_up + (std::max)(steadiness, 1.0f) * g_breath + g_jolt;
    }
}

AimingOverhaul::Aim::Offset AimingOverhaul::Sway::Drift(float seconds) {
    g_time += seconds;
    // The breath keeps its place in the cycle as the heart quickens or slows it.
    const Pace pace = Paced(Heart::Racing());
    g_breathTime *= pace.Length() / g_pace.Length();
    g_pace = pace;
    // A held breath settles to the bottom and rests there, and goes on from the rest when let out.
    const bool held = Breath::Held();
    const double was = g_breathTime;
    if (held) {
        g_breathTime = pace.in + pace.out + kRestSettle;
    } else {
        g_breathTime = std::fmod(g_breathTime + seconds, pace.Length());
    }
    g_turned = held                                         ? Turn::None
               : g_breathTime < was                         ? Turn::In
               : was < pace.in && pace.in <= g_breathTime   ? Turn::Out
                                                            : Turn::None;
    g_lift = Follow(g_lift, held ? 0.0f : Lift(g_breathTime, pace), seconds,
                    held ? kSettle : kFollowBreath);
    if (!g_enabled) {
        return {};
    }
    const float racing = Heart::Racing();
    const float wider = 1.0f + kRacingSway * racing;
    const float calm = Calm(g_breathTime, pace) * wider;
    g_right = calm * Sum(kRight);
    g_up = calm * Sum(kUp);
    g_breath = wider * kBreathDepth * (2.0f * g_lift - 1.0f);
    g_jolt = (kRestingJolt + (kRacingJolt - kRestingJolt) * racing) * Heart::Jolt();
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
