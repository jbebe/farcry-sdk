// The first-person weapon's draws: its depth, drawn a second time into a texture of our own, and
// the pass its colour is drawn in.
//
// See docs/docs/engine-internals/presentation-and-input.md for the passes.
#pragma once

#include <cstdint>
#include <d3d9.h>

namespace WeaponOverhaul::WeaponDraws {

struct Depth {
    // Hardware depth at half the scene's size over the whole depth range; one where the weapon is
    // not. Borrowed until ReleaseDeviceObjects.
    IDirect3DTexture9* texture;
    UINT width;
    UINT height;
    // From the projection the weapon was drawn with: one over the tangent of half the vertical
    // field of view, and the depth offset that turns stored depth into dioptres.
    float verticalScale;
    float depthOffset;
};

// Takes over the device's indexed draws. False, and logged, when it cannot.
bool Install();

// Whether the draws are watched at all, which costs every draw one compare while off.
void SetWatching(bool watching);

// False unless this frame's weapon depth was drawn.
bool Latest(Depth& out);

// The serial of the pass the weapon's colour was first drawn in this frame, or none.
uint32_t ColourPass();

void ReleaseDeviceObjects();

}
