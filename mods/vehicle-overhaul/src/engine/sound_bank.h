// Sound banks, which the game loads only for the sounds its data names.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::SoundBank {

// False, and logged, when this build lacks the bank lookup; Hold then does nothing.
bool Install();

// Loads the bank named after sound `id` and keeps it loaded for good. A sound the data does not name
// otherwise fails to play: a play loads nothing. Game thread.
void Hold(uint32_t id);

}
