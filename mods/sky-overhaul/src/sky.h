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
// horizon, plenty is a white one. The weather adds to whatever this asks for. None still leaves the
// little that clean air always carries, because that is what makes the sun's half of a low sky
// brighter than the far half.
void SetHaze(int percent);

// How bright the sunlight reaching the air is, as a percentage of what it takes to expose a clear
// noon sky.
void SetBrightness(int percent);

// How far a low sun's horizon turns from the sun's own warm hue to the far side's as the eye comes
// round, as a percentage. Zero leaves the horizon one colour all the way round. A high sun is never
// affected, whatever this is set to.
void SetHorizonGradient(int percent);

}
