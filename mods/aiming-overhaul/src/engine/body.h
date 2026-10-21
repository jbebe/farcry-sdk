// The player's body, rolled about its feet: the eye, the arms and the gun, and where a shot leaves
// from, all follow the pawn entity's orientation.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Body {

// False, and logged, when the calls that turn an entity are not found.
bool Install();

// Rolls the pawn's entity by `radians`, right positive, keeping its heading and position. Nought
// stands it upright once and then leaves it alone.
void Roll(uint8_t* pawn, float radians);

}
