// The scope's shadow: the dark crescent that comes in from the lens's rim as the hand drifts and
// the eye leaves the scope's axis.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::ScopeShadow {

// Draws over the scope's lens as the gun's colour pass ends, before the bloom reads the frame.
void OnGunPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
