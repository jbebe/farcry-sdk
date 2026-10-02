// The first-person weapon's draws: its depth, drawn a second time into a texture of our own, and
// the end of the pass its colour is drawn in, where effects on the gun are drawn.
//
// See docs/docs/engine-internals/presentation-and-input.md for the passes.
#pragma once

#include "engine/frame.h"

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

using GunPassFn = void (*)(const Frame::Pass& pass, const Depth& depth);

// Takes over the frame and the device's indexed draws. While the player is down the sights or a
// scope, `onGunPass` runs as the gun's colour pass ends, with this frame's gun depth. False, and
// logged, when either cannot be hooked.
bool Install(GunPassFn onGunPass);

void ReleaseDeviceObjects();

}
