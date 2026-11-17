// A vertex shader's bytecode rewritten to draw far things flatter.
#pragma once

#include <windows.h>

#include <vector>

namespace SkyOverhaul::VertexPatch {

// The registers a world shader projects its position with: the view-projection at c4, which takes
// world positions, or the rotation-only one at c0, which takes positions relative to the camera.
constexpr int kWorldMatrix = 4;
constexpr int kCameraMatrix = 0;

// Distances across the ground, in metres, between which far things are drawn flatter, and how many
// times farther the farthest look as a result.
struct Flatten {
    float start;
    float end;
    float stretch;
};

// `tokens` with the position it projects drawn toward the eye's height, by as much as a thing that
// far would look `stretch` times farther at `end`, easing in from `start`. Empty unless the clip
// position is four dp4 of one temp into o0 by one of the two matrices above.
std::vector<DWORD> Flattened(const DWORD* tokens, size_t count, const Flatten& flatten);

}
