// The scope's shadow: the dark crescent that comes in from the lens's rim as the look turns and the
// scope trails it.
#pragma once

#include "engine/weapon_draws.h"
#include "scope_lens.h"

namespace WeaponOverhaul::ScopeShadow {

// Draws over the lens ScopeLens found as the gun's colour pass ends, before the bloom reads the
// frame, and only through the glass: where `walls` show no housing.
void OnGunPass(const Frame::Pass& pass, const ScopeLens::Walls& walls);

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
