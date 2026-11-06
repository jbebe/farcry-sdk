// Where a first-person shot leaves from and which way it goes, which can be changed for the
// player's.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Shot {

// Moves the shot `pawn` fires from `origin` along `direction`, a unit vector, both in the world, if
// it is to leave from elsewhere or go another way.
using MoveFn = void (*)(uint8_t* pawn, float origin[3], float direction[3]);

// False, and logged, when the shot cannot be hooked.
bool Install(MoveFn move);

// The bullet strategy whose first-person shot this thread is setting up, or null.
uint8_t* SettingUp();

}
