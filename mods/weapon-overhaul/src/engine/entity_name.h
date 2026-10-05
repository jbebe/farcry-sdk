// The name a weapon's entity has from its archetype, as `weapons.Primary.AS50`.
#pragma once

#include <cstdint>
#include <string_view>

namespace WeaponOverhaul::EntityName {

// Copies the name of the weapon's entity, or leaves it empty where the engine's pointers do not
// hold.
void Read(uint8_t* weapon, char (&name)[64]);

// Whether the name is the archetype's own, or one of its variants under it.
bool Is(std::string_view name, std::string_view archetype);

}
