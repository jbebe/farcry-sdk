// The scope's lens: the opening a scope's housing leaves, found on screen every frame while a scope
// is up.
#pragma once

#include "engine/weapon_draws.h"

namespace WeaponOverhaul::ScopeLens {

// The smallest radius taken for a lens, in screen heights; anything smaller is the gun itself.
inline constexpr float kSmallest = 0.1f;

// What the lens is found in: a texture at the gun depth's size whose red is below `below` wherever
// the scope's housing is, and how far past the lens's radius its glass may reach, in lens radii.
struct Walls {
    IDirect3DTexture9* texture;
    float below;
    float reach;
};

// Finds the lens in `walls` as the gun's depth pass ends.
void Track(const Frame::Pass& pass, const Walls& walls);

// The lens found this frame, null before then: one texel holding its radius and its centre off the
// screen's, in screen heights, y down. Borrowed until ReleaseDeviceObjects.
IDirect3DTexture9* Found();

void ReleaseDeviceObjects();

}
