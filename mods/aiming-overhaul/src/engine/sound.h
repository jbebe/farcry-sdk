// Playing a sound event by id through the game's sound system.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Sound {

// Logs when this build lacks the sound system calls; everything below is then a no-op.
void Install();

// Requests the bank named after `id` and holds it loaded: a play never loads its own bank. Later
// calls for the same id do nothing.
void Hold(uint32_t id);

// Plays the event as the sound type given, an index into config\soundconfig.xml's types.
void Play(uint32_t id, int32_t type);

}
