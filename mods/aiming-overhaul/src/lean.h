// Leaning: held Q or E rolls the body out to the side, eye, gun and all.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Lean {

// Follows the lean signals.
void OnSignal(uint32_t signal);

// The body's roll this frame in radians, right positive, given the seconds since the last and
// whether the player may lean now.
float Roll(float seconds, bool free);

void SetEnabled(bool enabled);

}
