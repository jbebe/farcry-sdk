// Where a weapon's scope is in the mesh the engine draws it from: the vertex buffer of its nearest
// detail, and where the triangles of its SCOPE_HI part start in it.
//
// See docs/docs/engine-internals/first-person-aiming.md, "Where the scope is drawn from".
#pragma once

#include <cstddef>
#include <cstdint>

namespace AimingOverhaul::WeaponMesh {

inline constexpr size_t kMostScopeDraws = 8;

struct ScopePart {
    // The engine's resource for the nearest detail's vertices.
    uint8_t* vertices;
    // The first index of each of the part's draws at that detail.
    uint32_t starts[kMostScopeDraws];
    size_t startCount;

    // Whether a draw from this first index is one of the part's.
    bool Draws(uint32_t start) const;
};

// The scope part in the mesh of the weapon's entity, on the game thread. False where it has none,
// or the engine's objects do not hold.
bool Read(void* entity, ScopePart& part);

// The Direct3D vertex buffer the engine's resource holds now, compared and never used; null while it
// holds none.
const void* VertexBuffer(uint8_t* resource);

}
