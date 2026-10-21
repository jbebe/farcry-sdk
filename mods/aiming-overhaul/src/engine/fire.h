// Whether a weapon can fire, which can be refused for the one in the player's hands.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Fire {

// Whether the weapon wielded by `pawn` is refused a shot now.
using RefuseFn = bool (*)(uint8_t* pawn);

// False, and logged, when the check cannot be hooked.
bool Install(RefuseFn refuse);

}
