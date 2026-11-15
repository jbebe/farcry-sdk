// What the weapon in hand weighs, from its archetype's entity data.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Weight {

// Reads the weight of the weapon now in hand, or null. Call each time the weapon changes.
void Follow(uint8_t* weapon);

// The weapon in hand's weight, in kilograms.
float Kilograms();

}
