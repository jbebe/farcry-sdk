// The colour the land fades into at distance, changed on its way to the shaders: as the sun sets,
// the end of the fog's ramp toward it takes the far end's colour and both dim.
#pragma once

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::FogTint {

// Takes over the constant uploads. Call once from FCSE_Load. False means the entry points could not
// be found, proved or taken, which it logs, and nothing is left hooked.
bool Install();

// How far the sun has set, from nought while it is up to one once the land lies in the earth's
// shadow, how bright the land's fog is kept by then against the engine's, and toward the sun.
// Published once a frame from the sky.
void SetDusk(float dusk, float brightness, const float sun[3]);

// Call before each of the engine's draws. Puts the land's fog back over the engine's wherever the
// engine restored its own some way the setters never saw.
void BeforeDraw(IDirect3DDevice9* device);

// The fog colour and range as the engine last set them, before they were changed; left as they are
// where the engine has set nothing yet. The sky and clouds meet the horizon in these.
void Engine(float colour[3], float range[3]);

// Stops changing anything until a dusk is published again, for when there is no sky of ours.
void Forget();

// How many times the land's fog has been written, and how many of those put it back over the
// engine's own found before a draw.
uint32_t TintCount();
uint32_t RestoreCount();

}
