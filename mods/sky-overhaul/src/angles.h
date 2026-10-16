// Angles as the tuning window gives them and as the maths takes them.
#pragma once

namespace SkyOverhaul {

constexpr float kPi = 3.14159265f;

constexpr float Radians(float degrees) {
    return degrees * kPi / 180.0f;
}

}
