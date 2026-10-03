// The scope's shadow: the dark crescent that comes in from the lens's rim as the look turns and the
// scope trails it.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::ScopeShadow {

// In lens radii, x right and y down.
struct Shift {
    float x;
    float y;
};

// Finds the scope's lens as the gun's colour pass ends, as the hole the housing leaves in the gun's
// depth where nothing is nearer than the stored depth `hole`: one for the scope as the engine draws
// it, less where it is cut. While the shadow is on, draws it over the lens before the bloom reads
// the frame.
void OnGunPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth, float hole);

// The lens found this frame, null before then: one texel holding its radius and its centre off the
// screen's, in screen heights, y down. Borrowed until ReleaseDeviceObjects.
IDirect3DTexture9* FoundLens();

// How far the shadow's clear circle, the scope's far end, has swung off the lens's centre with the
// look's speed. Nought while the shadow is off.
Shift Swing();

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
