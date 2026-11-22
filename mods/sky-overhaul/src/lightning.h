// Lightning in the weather's storms: strikes born in the raining hearts of mature cells and under a
// stormy air mass, each a few flickering pulses that light the cloud from inside, some a bolt to the
// ground, and a flash that src/engine/environment.cpp puts into the world's own light.
#pragma once

#include "engine/frame.h"

#include <d3d9.h>

namespace SkyOverhaul::Lightning {

// Listens for test strikes on FCSE's `skyoverhaul` channel. Call once from FCSE_Load.
void Install();

// From fcse.ini's Weather mode: no strikes while the weather is off.
void SetEnabled(bool enabled);

// Moves the strikes on and draws the bolts into the sky pass, after the clouds.
void OnScenePass(const Frame::Pass& pass);

// The light the strikes now throw on the world round the eye, by how near each is, in the units of
// the scene's ambient light. Any thread.
float WorldFlash();

// Plays the thunder now due, on the game's thread, from the environment manager's frame.
void OnGameFrame();

// The brightest strikes now, for the clouds to glow round: xyz where, w how bright, nought for none.
inline constexpr unsigned kFlashes = 2;
void Flashes(float out[kFlashes * 4]);

void ReleaseDeviceObjects();

}
