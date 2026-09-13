// Clouds of our own, drawn into the world's sky pass where the engine's used to be.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Clouds {

// Prepares the effect and starts the noise generating. Call once from FCSE_Load, after the frame
// is being followed. Drawing only starts once it is enabled and the noise is finished.
void Install();

// Runs on each of the frame's scene passes; draws on the sky one.
void OnScenePass(const Frame::Pass& pass);

// Draws the rectangle, in pixels, at `depth` through the clouds as last drawn, discarding its
// pixels as far as the clouds cover them. False while no clouds are being drawn.
bool DrawCover(IDirect3DDevice9* device, float left, float top, float right, float bottom,
               float depth);

// Draws the clouds' cover into the god-ray mask, multiplying it by what the clouds let through.
// False while no clouds are being drawn.
bool DrawMask(IDirect3DDevice9* device);

// Surrenders what belongs to the device, before it is reset.
void ReleaseDeviceObjects();

// Whether to draw at all. The engine's own clouds are silenced separately, so that both can be off
// at once and the sky compared against either.
void SetEnabled(bool enabled);

}
