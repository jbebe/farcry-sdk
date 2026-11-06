// What perception found of the player, for the features that act on it.
#pragma once

#include <cstdint>

namespace DifficultyOverhaul::Perception {

// Whether the soldier, by entity id, has lately seen enough of the player in the open to fire at, with
// nothing hiding his head; `now` by game clock.
bool InTheOpen(uint32_t soldier, double now);

}
