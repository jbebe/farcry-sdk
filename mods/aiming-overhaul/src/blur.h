// The gun out of focus down the iron sights, the eye focused on the front sight.
#pragma once

#include "engine/weapon_draws.h"

namespace AimingOverhaul::Blur {

// The constant BlurPS, in src/shaders/blur.fx, reads its step from.
inline constexpr UINT kStep = 1;

// Draws over the gun as its colour pass ends, before the bloom reads the frame.
void OnGunPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
