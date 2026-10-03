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

// In screen heights, the centre off the screen's, y down.
struct Lens {
    float radius;
    float x;
    float y;
};

// Draws over the scope's lens as the gun's colour pass ends, before the bloom reads the frame. The
// lens is `known`, all of it glass, or else found as the hole the housing leaves in the gun's depth.
void OnGunPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth, const Lens* known);

// How far the shadow's clear circle, the scope's far end, has swung off the lens's centre with the
// look's speed, less the step it takes at the lightest turn. Nought while the shadow is off.
Shift Swing();

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
