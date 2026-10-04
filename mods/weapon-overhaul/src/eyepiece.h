// A scope seen from its eyepiece at the same size for every scope: the engine's housing gives way to
// a black, out-of-focus body with a mount below it, the world fills the opening, and the reticle
// grows with the opening, black and soft.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::Eyepiece {

// Measures a scope from its depth once it has come up and settled, and keeps what it found for the
// next time it comes up.
void OnDepthPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth);

// Drops the scope's housing, sends the reticle's draws into a mask of its own, and puts back what
// that changed.
bool BeforeGunDraw(IDirect3DDevice9* device, const WeaponDraws::Projection& projection,
                   bool depthPass);
void AfterGunDraw(IDirect3DDevice9* device);

// Lays the scope's body and the reticle over the finished frame, black and soft.
void OnComposite(IDirect3DDevice9* device);

// The opening's radius in screen heights while a scope is drawn this way, nought otherwise.
float OpeningRadius();

// Whether a scope is cut to its eyepiece this frame.
bool Active();

// The stored depth the scope is cut at this frame, one while it is drawn whole.
float Hole(const WeaponDraws::Projection& projection);

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
