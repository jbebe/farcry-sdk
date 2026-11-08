// The physics world's hits along a ray: the first among the layers soldiers' sight rays meet, or the first
// that is no body among everything a bullet meets.
#pragma once

#include <cstdint>

namespace DifficultyOverhaul::Ray {

// SPhysRayHit: where a ray met a surface, the physics body it belongs to, and the static geometry if it is
// some.
struct Hit {
    float point[3];
    float normal[3];
    uint32_t body;
    float distance;
    uint8_t part;
    uint8_t material;
    uint32_t treePart;
    uint32_t tree;
    uint32_t unused;
};
static_assert(sizeof(Hit) == 0x30);

// False, and logged, when the ray cast is not found.
bool Install();

// Whether nothing is within `length` of `from` along the unit vector `direction`. The game thread
// only.
bool Clear(const float from[3], const float direction[3], float length);

// The nearest hit within `length` of `from` along the unit vector `direction` that is not on a body,
// among the ground, walls, objects and vehicles a bullet hits; false when there is none. The game thread
// only.
bool FirstSurface(const float from[3], const float direction[3], float length, Hit& hit);

// Whether a hit is on a body or a head, by its logic material.
bool OnBody(const Hit& hit);

// The physics body a hit belongs to, or null, and whether a body is the terrain.
uint8_t* Body(const Hit& hit);
bool IsTerrain(uint8_t* body);

}
