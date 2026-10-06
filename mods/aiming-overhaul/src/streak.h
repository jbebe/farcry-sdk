// The tracers' streaks drawn with a shader of the plugin's own in place of the engine's textured
// quad, so they stay a smooth glow at any angle and size.
#pragma once

namespace AimingOverhaul::Streak {

// Hooks the device calls the streaks are told apart and drawn by. False, and logged, when they
// cannot be hooked; the streaks are then drawn with their texture.
bool Install();

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
