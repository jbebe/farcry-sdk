// Leaning: held Q or E rolls the view to the side, the arms and the gun with it. The eye does not
// move out yet; see docs/docs/engine-internals/first-person-aiming.md#leaning.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Lean {

// Follows the lean signals.
void OnSignal(uint32_t signal);

// The view's roll this frame in radians, right positive, given the seconds since the last and
// whether the player may lean now.
float Roll(float seconds, bool free);

void SetEnabled(bool enabled);

}
