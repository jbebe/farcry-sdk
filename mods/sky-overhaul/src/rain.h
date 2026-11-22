// Rain drops of our own in place of the game's: thousands of streaks round the player, falling along
// the game's rain at its strength, hidden by the world and under cover as the game's were.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Rain {

// From fcse.ini's Rain mode. Off gives the game its own drops back.
void SetEnabled(bool enabled);

// Draws into the sky pass, before the weapon and the blended draws that follow it.
void OnScenePass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
