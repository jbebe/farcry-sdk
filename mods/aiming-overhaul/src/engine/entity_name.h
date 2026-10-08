// The name a weapon's entity has from its archetype, as `weapons.Primary.AS50`.
#pragma once

#include <cstdint>

namespace AimingOverhaul::EntityName {

// Copies the name of the weapon's entity, or leaves it empty where the engine's pointers do not
// hold.
void Read(uint8_t* weapon, char (&name)[64]);

}
