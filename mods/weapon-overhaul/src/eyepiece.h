// A scope seen from its eyepiece: everything past the scope's first centimetre is cut away so the
// world fills the opening, the ring left of it is black and out of focus, and the reticle grows
// with the opening, black and soft.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::Eyepiece {

// Measures a scope from its depth once it has come up and settled, and keeps what it found for the
// next time it comes up.
void OnDepthPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

// Cuts one of the gun's draws, sends the reticle's into a mask of its own, or drops them, and puts
// back what that changed.
bool BeforeGunDraw(IDirect3DDevice9* device, const WeaponDraws::Projection& projection,
                   bool depthPass);
void AfterGunDraw(IDirect3DDevice9* device);

// Lays the ring left of the scope and the reticle over the finished frame, black and soft.
void OnComposite(IDirect3DDevice9* device, const WeaponDraws::Depth& depth);

// Whether a scope is cut to its eyepiece this frame.
bool Active();

// The stored depth the scope is cut at this frame, one while it is drawn whole.
float Hole(const WeaponDraws::Projection& projection);

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
