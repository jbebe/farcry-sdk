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

}
