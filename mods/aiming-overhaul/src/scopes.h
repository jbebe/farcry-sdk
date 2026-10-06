// The scopes drawn from their eyepiece, by the weapon that carries them, with what was measured off
// their meshes from the eye and the reticle each is drawn with.
#pragma once

#include <d3d9.h>

#include <span>

namespace AimingOverhaul::Scopes {

struct Reticle {
    // The image's resource name in src/aiming_overhaul.rc: square, its centre on the look's and its
    // edge on the lens's.
    const char* image;
    // Whether it is lit from within, as a holographic sight's is.
    bool illuminated;
};

struct Scope {
    // The weapon's archetype, which its entity is named after.
    const char* weapon;
    // How many vertices the weapon's larger draws take, all of its nearest detail's buffer, which
    // tells that buffer from anything else drawn in first person.
    UINT vertices;
    // How many triangles the scope's housing draws in the gun's depth pass, a draw only the sight
    // picture makes.
    UINT housing;
    // The black rim around the opening, as a share of the opening's radius.
    float rim;
    // The lens's centre, in lens radii from the look's centre, x right and y up.
    float lensX, lensY;
    const Reticle* reticle;
    // The eyepiece's own shape as a distance field, kShapeSize square; empty for a plain ring.
    std::span<const BYTE> shape;
    // How far the eye comes forward as the scope is raised, in metres.
    float raiseReach;
    // How big the scope is drawn, as a share of the size every scope is drawn at.
    float size = 1.0f;
};

// The distance field's side in texels, its width about the look's centre, and the distances its
// bytes span, both in lens radii. Inside the eyepiece is above the middle byte.
inline constexpr UINT kShapeSize = 128;
inline constexpr float kShapeWidth = 4.0f;
inline constexpr float kShapeSpan = 0.5f;

// The scope of the weapon whose entity has this name, if it is one of them.
const Scope* Find(const char* weaponName);

}
