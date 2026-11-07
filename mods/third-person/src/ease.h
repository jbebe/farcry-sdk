// Easing a value over time.
#pragma once

#include <algorithm>
#include <cmath>

namespace ThirdPerson {

// Smoothstep, nought to one.
inline float Smooth(float t) {
    t = std::clamp(t, 0.0f, 1.0f);
    return t * t * (3.0f - 2.0f * t);
}

// The share of the way to a target that a lag of `lag` seconds closes over `seconds`.
inline float Share(float seconds, float lag) {
    return 1.0f - std::exp(-seconds / lag);
}

// `value` moved toward `target` as a lag of `lag` seconds would over `seconds`.
inline float Ease(float value, float target, float seconds, float lag) {
    return value + (target - value) * Share(seconds, lag);
}

}
