// The colour the whole world fades into at distance, changed on its way to the shaders that use it.
//
// The horizon a player sees is not the sky. It is the land, fogged out, and the engine fogs it to a
// colour of its own - so a sky recoloured on its own meets that land at a seam, and the colour that
// wins is the engine's. This intercepts the two registers that colour is carried in and puts the
// sky's own horizon there, so the terrain, the water, the engine's sky and both of our layers all
// arrive at one colour.
#pragma once

#include <cstdint>

namespace SkyOverhaul::FogTint {

// Takes over the constant uploads. Call once from FCSE_Load. False means the entry points could not
// be found, proved or taken, which it logs, and nothing is left hooked.
bool Install();

// What the sky comes to at the horizon, looking along the engine's own fog heading and against it -
// the two ends of the ramp the engine already colours its fog by. Published once a frame from the
// sky, which is the only thing that knows.
void SetHorizon(const float toward[3], const float away[3]);

// Stops replacing anything until a horizon is published again, for when there is no sky of ours to
// agree with.
void Forget();

// How far the world's fog is carried from the engine's own colour toward the sky's, as a
// percentage. Zero leaves every constant exactly as the engine set it.
void SetMatch(int percent);

// How many uploads have been changed. A count that stops climbing is a fog colour that is being
// reached once and then written over, which is the failure that looks like a slider doing nothing.
uint32_t TintCount();

}
