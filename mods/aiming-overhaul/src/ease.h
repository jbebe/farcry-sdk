// Easing a value over time.
#pragma once

#include <algorithm>
#include <cmath>

namespace AimingOverhaul {

// Smoothstep, nought to one.
inline float Ease(float linear) {
    return linear * linear * (3.0f - 2.0f * linear);
}

// `value` moved toward `target` by at most `step`.
inline float Approach(float value, float target, float step) {
    return target > value ? (std::min)(value + step, target) : (std::max)(value - step, target);
}

// `value` moved toward `target` as a lag of `lag` seconds would over `seconds`.
inline float Follow(float value, float target, float seconds, float lag) {
    return value + (target - value) * (1.0f - std::exp(-seconds / lag));
}

// A damped spring: where it is and how fast it moves.
struct Spring {
    float x = 0.0f;
    float v = 0.0f;

    // Steps toward `target` over `seconds`, ringing at `omega` radians a second, damped by `zeta`,
    // in steps short enough to stay steady; at rest once it is close to a target of nought.
    void Step(float target, float omega, float zeta, float seconds) {
        constexpr float kLongestStep = 1.0f / 240.0f;
        const int steps = (std::max)(1, static_cast<int>(std::ceil(seconds / kLongestStep)));
        const float dt = seconds / static_cast<float>(steps);
        for (int i = 0; i < steps; ++i) {
            v += (omega * omega * (target - x) - 2.0f * zeta * omega * v) * dt;
            x += v * dt;
        }
        if (target == 0.0f && std::fabs(x) < 1e-4f && std::fabs(v) < 1e-3f) {
            x = v = 0.0f;
        }
    }
};

}
