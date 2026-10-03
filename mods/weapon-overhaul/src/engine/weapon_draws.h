// The first-person weapon's draws: its depth, drawn a second time into a texture of our own, the end
// of the passes its depth and colour are drawn in, and each of its draws as it happens.
//
// See docs/docs/engine-internals/presentation-and-input.md for the passes.
#pragma once

#include "engine/frame.h"

#include <d3d9.h>

namespace WeaponOverhaul::WeaponDraws {

// The projection the weapon is drawn with.
struct Projection {
    // One over the tangent of half the field of view, across and up.
    float horizontalScale;
    float verticalScale;
    // The two terms of stored depth, depthScale + depthOffset / metres.
    float depthScale;
    float depthOffset;

    float Stored(float metres) const { return depthScale + depthOffset / metres; }
    float Metres(float stored) const { return depthOffset / (stored - depthScale); }
};

struct Depth {
    // Hardware depth at half the scene's size over the whole depth range, of the gun as the engine
    // draws it, before any listener's changes; one where the weapon is not. Borrowed until
    // ReleaseDeviceObjects.
    IDirect3DTexture9* texture;
    UINT width;
    UINT height;
    Projection projection;
};

using GunPassFn = void (*)(const Frame::Pass& pass, const Depth& depth);
// `depthPass` is whether the draw is of the gun's depth rather than its colour. False drops the
// draw.
using GunDrawFn = bool (*)(IDirect3DDevice9* device, const Projection& projection, bool depthPass);
using RestoreFn = void (*)(IDirect3DDevice9* device);

struct Listener {
    // As the first pass with the gun's depth in it ends, the depth complete but for cut-outs, which
    // come in a pass of their own.
    GunPassFn onDepthPass;
    // As the gun's colour pass ends.
    GunPassFn onGunPass;
    // Around each of the gun's draws, depth and colour; `afterDraw` puts back what `beforeDraw`
    // changed, and is not called for a dropped draw.
    GunDrawFn beforeDraw;
    RestoreFn afterDraw;
};

// Takes over the frame and the device's indexed draws, and calls `listener` while the player is
// down the sights or a scope. False, and logged, when either cannot be hooked.
bool Install(const Listener& listener);

void ReleaseDeviceObjects();

}
