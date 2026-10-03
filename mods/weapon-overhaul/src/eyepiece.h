// A scope seen from its eyepiece: everything past the scope's first centimetre is cut away so the
// world fills the opening, and the reticle grows with the opening and swings with the shadow.
#pragma once

#include "engine/weapon_draws.h"
#include "scope_shadow.h"

namespace WeaponOverhaul::Eyepiece {

// Measures a scope from its depth once it has come up and settled, and keeps what it found for the
// next time it comes up.
void OnDepthPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

// Cuts or grows one of the gun's draws, or drops it, and puts back what that changed.
bool BeforeGunDraw(IDirect3DDevice9* device, const float* projection, bool depthPass);
void AfterGunDraw(IDirect3DDevice9* device);

// The opening this frame, while a scope is cut to its eyepiece.
bool Opening(const WeaponDraws::Depth& depth, ScopeShadow::Lens& lens);

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
