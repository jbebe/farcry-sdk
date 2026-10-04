// The scope's shadow: the dark crescent that comes in from the lens's rim as the look turns and the
// scope trails it.
#pragma once

#include "engine/frame.h"

namespace WeaponOverhaul::ScopeShadow {

// Draws over the eyepiece's opening as the gun's colour pass ends, before the bloom reads the frame.
void OnGunPass(const Frame::Pass& pass);

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
