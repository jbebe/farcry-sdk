// The scopes drawn from their eyepiece, by the weapon that carries them, with what was measured off
// their meshes from the eye.
#pragma once

#include <d3d9.h>

#include <span>

namespace WeaponOverhaul::Scopes {

// A point of a reticle: in lens radii from the look's centre, x right and y up, and where it
// samples the reticle's texture.
struct Point {
    float x, y;
    float u, v;
};

// Black wherever the texture's alpha passes, as the engine alpha-tests it; or the texture's own
// colours, blended by its alpha.
enum class Look { Black, Lit };

struct Piece {
    Look look;
    // Three points a triangle.
    std::span<const Point> triangles;
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
    std::span<const Piece> pieces;
    // The eyepiece's own shape as a distance field, kShapeSize square; empty for a plain ring.
    std::span<const BYTE> shape;
    // How far the eye comes forward as the scope is raised, in metres.
    float raiseReach;
};

// The distance field's side in texels, its width about the look's centre, and the distances its
// bytes span, both in lens radii. Inside the eyepiece is above the middle byte.
inline constexpr UINT kShapeSize = 128;
inline constexpr float kShapeWidth = 4.0f;
inline constexpr float kShapeSpan = 0.5f;

// The scope of the weapon whose entity has this name, if it is one of them.
const Scope* Find(const char* weaponName);

}
