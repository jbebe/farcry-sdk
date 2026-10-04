// The scopes drawn from their eyepiece, told apart by their housing's draw in the gun's depth pass,
// with what was measured off their meshes from the eye.
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
    UINT primitives;
    UINT vertices;
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

// The distance field's side in texels, how far it reaches either side of the look's centre, and the
// distances its bytes span, both in lens radii. Inside the eyepiece is above the middle byte.
inline constexpr UINT kShapeSize = 128;
inline constexpr float kShapeReach = 2.0f;
inline constexpr float kShapeSpan = 0.5f;

// The scope whose housing draws this much, if it is one of them.
const Scope* Find(UINT primitives, UINT vertices);

// The scope of the weapon whose meshes draw from this many vertices, if it is one of them.
const Scope* InHand(UINT vertices);

}
