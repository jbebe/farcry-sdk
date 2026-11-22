// The water's waves while it rains: a tiling normal map of the drops' rings, drawn once a frame,
// which water draws read in place of their own (src/wet.cpp).
#pragma once

#include "engine/frame.h"

#include <d3d9.h>

namespace SkyOverhaul::Ripples {

// Draws this frame's rings for the next frame's water, at the rain's strength and the rings' clock.
void OnScenePass(const Frame::Pass& pass, float rain, float clock);

// The map the last pass drew, or null when it rains no longer or the device would not take it.
IDirect3DTexture9* Texture();

void ReleaseDeviceObjects();

}
