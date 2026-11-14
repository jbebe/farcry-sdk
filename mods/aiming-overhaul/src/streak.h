// The tracers' streaks drawn with a shader of the plugin's own in place of the engine's textured
// quad, so they stay a smooth glow at any angle and size.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Streak {

// Hooks the engine device's draw the streaks are told apart and drawn in. False, and logged, when
// it cannot be hooked; the streaks are then drawn with their texture.
bool Install();

// Takes the streaks' texture from a trace's texture resource, a CTextureResource, as the trace is
// built; their draws are those that bind the Direct3D texture it holds.
void FollowTexture(const uint8_t* resource);

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
