// Grass lit by the sun through a vertex shader of ours, in place of the one the engine builds for
// itself: roots in shade, sides that turn to and from the sun, a shine along the blades, and a glow
// with the sun behind them. The engine's pixel shader still shadows, textures and fogs it.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Foliage {

// Takes over every grass and leaf draw. Call once from FCSE_Load.
void Install();

void SetEnabled(bool enabled);

// Logs how many grass draws were lit by ours since the last time.
void OnFinalPass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
