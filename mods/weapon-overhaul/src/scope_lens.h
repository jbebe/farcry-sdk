// The scope's lens: the opening a scope's housing leaves in the gun's depth, found on screen every
// frame while a scope is up.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::ScopeLens {

// The smallest radius taken for a lens, in screen heights; anything smaller is the gun itself.
inline constexpr float kSmallest = 0.1f;

// Finds the lens as the gun's depth pass ends, where nothing is nearer than the stored depth `hole`:
// one for the scope as the engine draws it, less where it is cut. Its radius is `opening` in screen
// heights in place of the one found, unless that is nought.
void Track(const Frame::Pass& pass, const WeaponDraws::Depth& depth, float hole, float opening);

// The lens found this frame, null before then: one texel holding its radius and its centre off the
// screen's, in screen heights, y down. Borrowed until ReleaseDeviceObjects.
IDirect3DTexture9* Found();

void ReleaseDeviceObjects();

}
