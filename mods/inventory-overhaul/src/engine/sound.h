// Playing and stopping a sound event by id through the game's sound system.
#pragma once

#include <cstdint>

namespace InventoryOverhaul::Sound {

// A handle no sound has.
constexpr uint32_t kNone = UINT32_MAX;

// False when this build lacks the sound system calls; everything below is then a no-op.
bool Install();

// Requests the bank named after `id` and holds it loaded: a play never loads its own bank. Once is
// enough; later calls do nothing.
void Hold(uint32_t id);

// The playing sound's handle, or kNone when it did not start.
uint32_t Play(uint32_t id, int32_t type);

// Fades the sound out over `fade` seconds, at once for 0.
void Stop(uint32_t handle, float fade);

// The sound event's length in seconds, negative while its bank is not loaded.
float Length(uint32_t id);

}
