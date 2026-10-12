// The headlight of the car the player drives lighting the grass, which the engine leaves dark: each
// of the engine's draws of grass lit by the sun is drawn again with the headlight's light added.
#pragma once

namespace VehicleOverhaul::GrassLight {

// False, and logged, when the draws cannot be followed.
bool Install();

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

}
