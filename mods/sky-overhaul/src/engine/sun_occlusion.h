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
// A position beyond the edge of the frame is drawn back to the edge nearest it. The depth buffer
// only covers what the frustum covers, so that is the closest anything can be measured to a sun
// that is off screen - near enough that a wall between the player and the sun still blocks it, and
// approximate enough that a steep downward view can read the ground as cover.
void Sample(IDirect3DDevice9* device, float centreX, float centreY, const D3DVIEWPORT9& viewport);

// Fraction of the sun's disc that reached the screen, 0 to 1. Negative when unknown - before the
// first answer arrives, or when the measurement itself came back degenerate. Callers must treat
// that as unoccluded: a measurement that cannot be trusted must not be allowed to suppress
// anything, or a silent failure here becomes a feature that silently does nothing.
//
// The last answer stands only while the sun is behind the camera, where there is nothing in the
// depth buffer to measure against at all.
float Visibility();

// The last pair of counts, for the log: how many pixels of the patch survived the depth test, and
// how many there were in total.
void LastCounts(unsigned long& reachedScreen, unsigned long& wouldHaveDrawn);

// Releases the queries, which belong to the device that made them.
void ReleaseDeviceObjects();

}
