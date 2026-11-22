// The small blends the sky, the weather and the passes share.
#pragma once

#include <algorithm>

namespace SkyOverhaul {

inline float Mix(float a, float b, float t) {
    return a + (b - a) * t;
}

// Smoothstep of `t` clamped to 0..1.
inline float Smooth(float t) {
    t = std::clamp(t, 0.0f, 1.0f);
    return t * t * (3.0f - 2.0f * t);
}

inline float Smooth(float from, float to, float value) {
    return Smooth((value - from) / (to - from));
}

// Brightness by the weights the engine's own luma uses.
inline float Luma(const float colour[3]) {
    return 0.299f * colour[0] + 0.587f * colour[1] + 0.114f * colour[2];
}

}
