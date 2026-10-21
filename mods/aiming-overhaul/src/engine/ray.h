// The physics world's first solid hit along a ray: what stops a bullet, characters left out.
#pragma once

namespace AimingOverhaul::Ray {

// False, and logged, when the ray cast is not found.
bool Install();

// How far along `delta` from `from` the first solid hit is, in metres, or the ray's whole length
// when nothing is hit. The game thread only.
float Cast(const float from[3], const float delta[3]);

}
