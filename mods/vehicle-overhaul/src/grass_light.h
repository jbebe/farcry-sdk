// Grass lit by the headlight. The engine draws a spot light's pass over grass but lights a clump by
// its ground's slope toward the light, which leaves grass a headlight skims across nearly black. Its
// own shaders are drawn with that one term wrapped round the blades, (n.l + 1) / 2.
#pragma once

namespace VehicleOverhaul::GrassLight {

// False, and logged, when the draws cannot be followed.
bool Install();

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

}
