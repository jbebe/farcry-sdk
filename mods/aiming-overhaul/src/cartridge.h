// The round the weapon in hand fires, from its archetype's `Cartridge.*` entity data, which
// Difficulty Overhaul's weapon fragments give it.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Cartridge {

// Follows the weapon in hand, or null, reading its round each time it changes.
void Follow(uint8_t* weapon);

// The push one round gives whoever fires it, in newton-seconds: its projectiles' momentum leaving
// the barrel; nought where the archetype names no round.
float Impulse();

// The round's name, as "7.62x39 mm"; empty where the archetype names none.
const char* Name();

}
