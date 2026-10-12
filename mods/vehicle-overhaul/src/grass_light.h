// Grass lit by the headlight. The engine draws a spot light's pass over grass, but scales the light by
// the fog's amount rather than by what the fog leaves, which leaves grass short of the fog unlit at
// all. Its own shaders are drawn with the fog turned round.
#pragma once

namespace VehicleOverhaul::GrassLight {

// False, and logged, when the draws cannot be followed.
bool Install();

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

}
