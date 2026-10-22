// The player's body, rolled about its feet from the camera update, which rolls the view; the arms
// and the gun were posed before it and do not follow.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Body {

// False, and logged, when the calls that turn an entity are not found.
bool Install();

// Rolls the pawn's entity by `radians`, right positive, keeping its heading and position. Nought
// stands it upright once and then leaves it alone.
void Roll(uint8_t* pawn, float radians);

}
