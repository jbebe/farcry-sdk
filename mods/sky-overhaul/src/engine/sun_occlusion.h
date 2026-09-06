// How much of the sun the player can actually see, measured against the scene.
//
// The engine has this number for its own flare and never publishes it, so it is measured the same
// way here. See docs/docs/engine-internals/sky-and-clouds.md.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::SunOcclusion {

// Measures this frame from the sun's position on screen, in pixels. Must be called while the
// scene's depth buffer is still attached: by the end of the frame it is gone.
//
// A position beyond the edge of the frame is slid back to the nearest edge rather than dropped,
// which is as close to an off-screen sun as the depth buffer reaches.
void Sample(IDirect3DDevice9* device, float centreX, float centreY, const D3DVIEWPORT9& viewport);

// Fraction of the sun's disc that reached the screen, 0 to 1, or negative when unknown. Callers
// must read unknown as unoccluded, so that a measurement that failed cannot suppress anything.
float Visibility();

// Releases the queries, which belong to the device that made them.
void ReleaseDeviceObjects();

}
