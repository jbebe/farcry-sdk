// A sky of our own, drawn where the engine draws its dome.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Sky {

// Prepares the effect and takes over the dome's draw. Call once from FCSE_Load. Nothing is drawn
// until it is enabled.
bool Install();

// Runs on each of the frame's scene passes. Draws nothing - the sky is drawn from inside the pass,
// not at the end of it - and only reports whether the dome is still being found.
void OnScenePass(const Frame::Pass& pass);

// Surrenders what belongs to the device, before it is reset.
void ReleaseDeviceObjects();

// Whether our sky replaces the engine's dome.
void SetEnabled(bool enabled);

// How much dust and water the air carries, as a percentage: none is a hard blue sky over a sharp
// horizon, plenty is a white one. The weather adds to whatever this asks for.
void SetHaze(int percent);

// How bright the sunlight reaching the air is, as a percentage of what it takes to expose a clear
// noon sky.
void SetBrightness(int percent);

}
