// The physics world's first solid hit along a ray, characters left out. See
// docs/docs/engine-internals/first-person-aiming.md#the-gun-near-walls.
#pragma once

namespace PlayerBody::Ray {

// False, and logged, when the ray cast is not found.
bool Install();

// How far from `from` along the unit vector `direction` the first solid hit is, in metres, or
// `length` when nothing is hit within it. Game thread.
float Cast(const float from[3], const float direction[3], float length);

}
