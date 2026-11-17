// A sky of our own, drawn where the engine draws its dome.
#pragma once

#include "engine/frame.h"
#include "tuning.h"

namespace SkyOverhaul::Sky {

// Prepares the effect and takes over the dome's draw. Call once from FCSE_Load. Nothing is drawn
// until it is enabled.
void Install();

// Draws the filled land under the horizon again over the pass our sky was drawn in, over the
// engine's sun.
void OnScenePass(const Frame::Pass& pass);

// How much the filled land shows and the sine of its top; none shows while the engine's sky is
// drawn.
void Fill(const Tuning::Values& values, float fill[2]);

// Surrenders what belongs to the device, before it is reset.
void ReleaseDeviceObjects();

// Whether our sky replaces the engine's dome.
void SetEnabled(bool enabled);

}
