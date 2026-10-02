// Playing a sound event by id through the game's sound system.
#pragma once

#include <cstdint>

namespace Flashlight::Sound {

// False, and logged, when this build lacks the sound system calls.
bool Install();

// Requests the bank named after `id` and holds it loaded: a play never loads its own bank. Once is
// enough; later calls do nothing.
void Hold(uint32_t id);

void Play(uint32_t id, int32_t type);

}
