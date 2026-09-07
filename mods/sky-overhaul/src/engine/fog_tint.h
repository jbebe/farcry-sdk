// The colour the whole world fades into at distance, changed on its way to the shaders that use it.
//
// The horizon a player sees is not the sky. It is the land, fogged out, and the engine fogs it to a
// colour of its own - so a sky recoloured on its own meets that land at a seam. This intercepts the
// two registers that colour is carried in, so the terrain, the water, the engine's own sky and both
// of our layers all fade into the same thing.
#pragma once

#include <cstdint>

namespace SkyOverhaul::FogTint {

// Takes over the constant uploads. Call once from FCSE_Load. False means the entry points could not
// be found, proved or taken, which it logs, and nothing is left hooked.
bool Install();

// How far the fog is carried from the engine's own colour toward the colour of dust, as a
// percentage. Zero leaves every constant exactly as the engine set it, which is what it means for
// this to be off.
void SetDust(int percent);

// How many uploads have been retinted. Zero while the dust is up is the one failure that would
// otherwise look like a colour that simply did not change.
uint32_t TintCount();

}
