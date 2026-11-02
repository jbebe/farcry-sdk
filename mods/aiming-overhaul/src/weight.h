// What the weapon in hand weighs, from its archetype's entity data.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Weight {

// Follows the weapon in hand, or null, reading its weight each time it changes.
void Follow(uint8_t* weapon);

// The weapon in hand's weight, in kilograms.
float Kilograms();

}
