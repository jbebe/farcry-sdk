// The colour the land fades into at distance, changed on its way to the shaders: as the sun sets,
// the end of the fog's ramp toward it takes the far end's colour and both dim.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::FogTint {

// How far the sun has set, from nought while it is up to one once the land lies in the earth's
// shadow, how bright the land's fog is kept by then against the engine's, and toward the sun.
// Published once a frame from the sky.
void SetDusk(float dusk, float brightness, const float sun[3]);

// Call before each of the engine's draws, with the engine's constants on the device. Writes the
// land's fog over whatever the engine uploaded since the last draw.
void BeforeDraw(IDirect3DDevice9* device);

// The fog colour and range as the engine last set them, while the registers hold ours; left as they
// are otherwise, since the registers then hold the engine's. The sky and clouds meet the horizon in
// these.
void Engine(float colour[3], float range[3]);

// Stops changing anything until a dusk is published again, for when there is no sky of ours.
void Forget();

}
