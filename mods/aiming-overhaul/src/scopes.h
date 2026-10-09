// The scopes drawn from their eyepiece: what the weapon's archetype says of its scope, and where the
// scope is in the weapon's mesh.
#pragma once

#include "engine/weapon_mesh.h"

#include <d3d9.h>

#include <span>

namespace AimingOverhaul::Scopes {

struct Scope {
    // The reticle's image under bin\plugins: square, its centre on the look's and its edge on the
    // lens's.
    char reticle[96];
    // Whether the reticle is lit from within, as a holographic sight's is.
    bool lit;
    // The eyepiece's own shape as a distance field, kShapeSize square; empty for a plain ring.
    std::span<const BYTE> shape;
    // The black rim around the opening, as a share of the opening's radius.
    float rim;
    // The lens's centre, in lens radii from the look's centre, x right and y up.
    float lensX, lensY;
    // How far the eye comes forward as the scope is raised, in metres.
    float raiseReach;
    // How big the scope is drawn, as a share of the size every scope is drawn at.
    float size = 1.0f;
    WeaponMesh::ScopePart part;
};

// The distance field's side in texels, its width about the look's centre, and the distances its
// bytes span, both in lens radii. Inside the eyepiece is above the middle byte.
inline constexpr UINT kShapeSize = 128;
inline constexpr float kShapeWidth = 4.0f;
inline constexpr float kShapeSpan = 0.5f;

// Follows the weapon in hand, or null, once a frame on the game thread, reading its scope each time
// it changes until it is read. Its scope, if the plugin draws it.
const Scope* Follow(uint8_t* weapon);

// The weapon in hand's scope as last followed, if the plugin draws it.
const Scope* InHand();

}
