// The physics world's first hit along a ray. See
// docs/docs/engine-internals/first-person-aiming.md#the-gun-near-walls.
#pragma once

namespace ThirdPerson::Ray {

// What a ray hits: what a bullet hits, characters left out, or only the ground and what stands
// fixed on it, which leaves out vehicles too.
enum class Layers { Bullet, Ground };

// False, and logged, when the ray cast is not found.
bool Install();

// How far from `from` along the unit vector `direction` the first hit is, in metres, or `length`
// when nothing is hit within it. Game thread.
float Cast(const float from[3], const float direction[3], float length, Layers layers = Layers::Bullet);

}
