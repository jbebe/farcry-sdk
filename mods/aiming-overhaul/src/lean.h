// Leaning: held Q or E rolls the body out to the side, eye, gun and all.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Lean {

// Radians at full lean, which takes the eye about 0.33 m to the side.
inline constexpr float kMostRoll = 0.21f;

// Follows the lean signals.
void OnSignal(uint32_t signal);

// The roll leaned to while no lean key is held, in radians, right positive.
void SetAuto(float roll);

// The body's roll this frame in radians, right positive, given the seconds since the last and
// whether the player may lean now.
float Roll(float seconds, bool free);

// The roll Roll last gave, and the one it was heading to.
float Current();
float Target();

void SetEnabled(bool enabled);

}
