// The first-person weapon's draws: its depth, drawn a second time into a texture of our own, the end
// of the passes its depth and colour are drawn in, and each of its draws as it happens.
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
    // field of view, and the two terms of stored depth, depthScale + depthOffset / metres.
    float verticalScale;
    float depthScale;
    float depthOffset;
};

using GunPassFn = void (*)(const Frame::Pass& pass, const Depth& depth);
// `projection` is the 16 floats at c8 to c11 the draw projects with, one register a row.
using GunDrawFn = void (*)(IDirect3DDevice9* device, const float* projection);
using RestoreFn = void (*)(IDirect3DDevice9* device);

struct Listener {
    // As the first pass with the gun's depth in it ends, the depth complete but for cut-outs.
    GunPassFn onDepthPass;
    // As the gun's colour pass ends.
    GunPassFn onGunPass;
    // Around each of the gun's draws, depth and colour; `afterDraw` puts back what `beforeDraw`
    // changed.
    GunDrawFn beforeDraw;
    RestoreFn afterDraw;
};

// Takes over the frame and the device's indexed draws, and calls `listener` while the player is
// down the sights or a scope. False, and logged, when either cannot be hooked.
bool Install(const Listener& listener);

void ReleaseDeviceObjects();

}
