// A sky of our own, drawn where the engine draws its dome.
#pragma once

namespace SkyOverhaul::Sky {

// Prepares the effect and takes over the dome's draw. Call once from FCSE_Load. Nothing is drawn
// until it is enabled.
void Install();

// Surrenders what belongs to the device, before it is reset.
void ReleaseDeviceObjects();

// Whether our sky replaces the engine's dome.
void SetEnabled(bool enabled);

}
