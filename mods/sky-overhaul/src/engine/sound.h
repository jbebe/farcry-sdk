// Playing a sound event by id through the game's sound system, from the game's own thread. See
// docs/docs/file-formats/spk.md#what-loads-the-child-bank-depload.
#pragma once

#include <cstdint>

namespace SkyOverhaul::Sound {

// False when the sound system's calls were not found in this build, which it logs.
bool Install();

// Asks for the bank holding `id` to be loaded and kept, once.
void Hold(uint32_t id);

// Plays `id` as a sound placed nowhere in the world: no position, no occlusion.
void Play(uint32_t id);

}
