// Clouds of our own, drawn into the world's sky pass where the engine's used to be.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Clouds {

// Prepares the effect and starts the noise generating. Call once from FCSE_Load, after the frame
// is being followed. Drawing only starts once it is enabled and the noise is finished.
void Install();

// Runs on each of the frame's scene passes; draws on the sky one.
void OnScenePass(const Frame::Pass& pass);

// Surrenders what belongs to the device, before it is reset.
void ReleaseDeviceObjects();

// Whether to draw at all. The engine's own clouds are silenced separately, so that both can be off
// at once and the sky compared against either.
void SetEnabled(bool enabled);

// Where the layer's floor sits and how deep it is, in metres.
void SetBaseAltitude(int metres);
void SetThickness(int metres);

// How much of the sky is filled, as a percentage.
void SetCoverage(int percent);

// How solid the cloud is where it is filled, and how hard its edges are torn, as percentages.
void SetDensity(int percent);
void SetDetail(int percent);

// How wide one repeat of the shape is, in metres, which is how large a single cloud reads.
void SetGrain(int metres);

// How fast the layer drifts, as a multiple of the wind the engine is already blowing.
void SetWind(int percent);

// Over how many metres a cloud turns into the colour of the horizon behind it.
void SetHaze(int metres);

// How much of the sky the high sheet of ice cloud reaches across, as a percentage. Zero leaves it
// out. How solid it is where it does reach is a separate thing.
void SetCirrus(int percent);
void SetCirrusOpacity(int percent);

}
