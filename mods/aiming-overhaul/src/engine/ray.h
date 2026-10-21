// The physics world's first solid hit along a ray: what stops a bullet, characters left out.
#pragma once

namespace AimingOverhaul::Ray {

// False, and logged, when the ray cast is not found.
bool Install();

// How far from `from` along the unit vector `direction` the first solid hit is, in metres, or
// `length` when nothing is hit within it. The game thread only.
float Cast(const float from[3], const float direction[3], float length);

// Whether nothing solid is within `length` of `from` along `direction`.
bool Clear(const float from[3], const float direction[3], float length);

}
