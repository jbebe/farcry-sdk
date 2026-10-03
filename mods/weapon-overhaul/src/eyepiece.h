// A scope seen from its eyepiece: everything past the scope's first centimetre is cut away so the
// world fills the opening, and the reticle grows with the opening.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::Eyepiece {

// Measures the scope from its uncut depth, once each time a scope comes up.
void OnDepthPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

// Cuts or grows one of the gun's draws, and puts back what that changed.
void BeforeGunDraw(IDirect3DDevice9* device, const float* projection);
void AfterGunDraw(IDirect3DDevice9* device);

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
