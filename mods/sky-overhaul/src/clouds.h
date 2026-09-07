// Clouds of our own, drawn into the world's sky pass where the engine's used to be.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Clouds {

// Prepares the effect. Call once from FCSE_Load, after the frame is being followed. Drawing only
// starts once it is enabled.
void Install();

// Runs on each of the frame's scene passes; draws on the sky one.
void OnScenePass(const Frame::Pass& pass);

// Surrenders what belongs to the device, before it is reset.
void ReleaseDeviceObjects();

// Whether to draw at all. The engine's own clouds are silenced separately, so that both can be off
// at once and the sky compared against either.
void SetEnabled(bool enabled);

void SetBaseAltitude(int metres);

// Which derivation of the camera the draw uses: the inverted view-projection, or the basis the
// engine publishes. They should agree, and the point of the switch is to see whether they do.
void SetUseBasis(bool useBasis);

}
