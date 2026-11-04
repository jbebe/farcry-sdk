// The physics world's first hit along a ray, among the layers soldiers' sight rays meet.
#pragma once

namespace DifficultyOverhaul::Ray {

// False, and logged, when the ray cast is not found.
bool Install();

// Whether nothing is within `length` of `from` along the unit vector `direction`. The game thread
// only.
bool Clear(const float from[3], const float direction[3], float length);

}
