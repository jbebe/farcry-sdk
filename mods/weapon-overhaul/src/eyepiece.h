// A scope seen from its eyepiece: everything past the scope's first centimetre is cut away so the
// world fills the opening, and the reticle grows with the opening and swings with the shadow.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::Eyepiece {

// Measures a scope from its depth once it has come up and settled, and keeps what it found for the
// next time it comes up.
void OnDepthPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

// Cuts or grows one of the gun's draws, or drops it, and puts back what that changed.
bool BeforeGunDraw(IDirect3DDevice9* device, const WeaponDraws::Projection& projection,
                   bool depthPass);
void AfterGunDraw(IDirect3DDevice9* device);

// Whether a scope is cut to its eyepiece this frame.
bool Active();

// The stored depth the scope is cut at this frame, one while it is drawn whole.
float Hole(const WeaponDraws::Projection& projection);

void SetEnabled(bool enabled);

// Whether the reticle swings with the shadow's clear circle, as it does while the shadow is on.
void SetSwinging(bool swinging);

void ReleaseDeviceObjects();

}
