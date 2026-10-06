// The first-person weapon's draws: its depth, drawn a second time into a texture of our own, the
// pass its colour is drawn in, and each of its draws as it happens.
//
// See docs/docs/engine-internals/presentation-and-input.md for the passes.
#pragma once

#include "engine/frame.h"

#include <d3d9.h>

namespace AimingOverhaul::WeaponDraws {

// The projection the weapon is drawn with.
struct Projection {
    // One over the tangent of half the field of view, up.
    float verticalScale;
    // The two terms of stored depth, depthScale + depthOffset / metres.
    float depthScale;
    float depthOffset;
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

// One of the gun's draws: how much it draws, and whether it is of the gun's depth rather than its
// colour.
struct Call {
    UINT primitiveCount;
    UINT numVertices;
    bool depthPass;
};

using GunPassFn = void (*)(const Frame::Pass& pass, const Depth& depth);
// False drops the draw.
using GunDrawFn = bool (*)(IDirect3DDevice9* device, const Call& call);
using DeviceFn = void (*)(IDirect3DDevice9* device);

struct Listener {
    // As the gun's colour pass begins, before its first draw, and as it ends.
    DeviceFn beforeColour;
    GunPassFn onGunPass;
    // Before each of the gun's draws, depth and colour.
    GunDrawFn beforeDraw;
    // As the frame's tone-mapped image is finished in the back buffer, under the interface.
    DeviceFn onComposite;
};

// Takes over the frame and the device's indexed draws, and calls `listener` while the player is
// down the sights or a scope, and for a few frames after a scope, which the render thread can still
// be drawing. False, and logged, when either cannot be hooked.
bool Install(const Listener& listener);

void ReleaseDeviceObjects();

}
