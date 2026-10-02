// Playing a sound event by id through the game's sound system.
#pragma once

#include <cstdint>

namespace Flashlight::Sound {

// Logs when this build lacks the sound system calls; everything below is then a no-op.
void Install();

// Requests the bank named after `id` and holds it loaded: a play never loads its own bank. Once is
// enough; later calls do nothing.
void Hold(uint32_t id);

void Play(uint32_t id, int32_t type);

}
