// Easing a value over time.
#pragma once

#include <algorithm>
#include <chrono>
#include <cmath>
#include <numbers>

namespace AimingOverhaul {

inline constexpr float kDegree = std::numbers::pi_v<float> / 180.0f;

// Seconds on a steady clock.
inline double Now() {
    return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch())
        .count();
}

// Smoothstep, nought to one.
inline float Ease(float linear) {
    return linear * linear * (3.0f - 2.0f * linear);
}

// How far along `seconds` is of `span` seconds, eased, from nought to one.
inline float Through(double seconds, float span) {
    return Ease(std::clamp(static_cast<float>(seconds) / span, 0.0f, 1.0f));
}

// How far a pulse is along `seconds` in, from nought to one: risen over `rise` seconds, clearing
// from `hold` seconds, gone by `gone`.
inline float Pulse(double seconds, float rise, float hold, float gone) {
    return Through(seconds, rise) * (1.0f - Through(seconds - hold, gone - hold));
}

// `value` moved toward `target` by at most `step`.
inline float Approach(float value, float target, float step) {
    return target > value ? (std::min)(value + step, target) : (std::max)(value - step, target);
}

// `value` moved toward `target` as a lag of `lag` seconds would over `seconds`.
inline float Follow(float value, float target, float seconds, float lag) {
    return value + (target - value) * (1.0f - std::exp(-seconds / lag));
}

// `value` as it is when small, eased toward `most` as it grows.
inline float SoftLimit(float value, float most) {
    return most * std::tanh(value / most);
}

// A damped spring: where it is and how fast it moves.
struct Spring {
    float x = 0.0f;
    float v = 0.0f;

    // Strikes it so that, from rest and damped not to swing, it peaks `peak` out 1 / omega seconds
    // later.
    void Strike(float peak, float omega) {
        v += peak * omega * std::numbers::e_v<float>;
    }

    // Steps toward nought over `seconds`, ringing at `omega` and damped not to swing past it,
    // exactly however long the step; at rest once it is close.
    void Settle(float omega, float seconds) {
        const float decay = std::exp(-omega * seconds);
        const float rising = v + omega * x;
        const float at = x + rising * seconds;
        x = at * decay;
        v = (rising - omega * at) * decay;
        Rest();
    }

private:
    void Rest() {
        if (std::fabs(x) < 1e-4f && std::fabs(v) < 1e-3f) {
            x = v = 0.0f;
        }
    }
};

}
