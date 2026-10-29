// Where a first-person shot leaves from, which can be moved for the player's.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Shot {

// Moves the shot `pawn` fires from `origin`, in the world, if it is to leave from elsewhere.
using MoveFn = void (*)(uint8_t* pawn, float origin[3]);

// False, and logged, when the shot cannot be hooked.
bool Install(MoveFn move);

}
