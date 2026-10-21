// Leaning: held Q or E rolls the upper body out to the side about the belly, so the eye swings
// sideways and a little down as the view rolls, and the gun goes with it.
#pragma once

#include "engine/aim.h"

#include <cstdint>

namespace AimingOverhaul::Lean {

// Radians at full lean, which takes the eye about 0.43 m to the side.
inline constexpr float kMostRoll = 0.44f;

// Follows the lean signals.
void OnSignal(uint32_t signal);

// The roll this frame in radians, right positive, given the seconds since the last, whether the
// player may lean now, and the roll to lean to while no lean key is held.
float Roll(float seconds, bool free, float autoRoll);

// The roll Roll last gave, and the one it was heading to.
float Current();
float Target();

// Where a roll takes the eye from where it would be upright, along the view's axes before the roll.
Aim::Offset Eye(float roll);

// How the eye moves, in the world, when the roll goes from `from` to `to`, facing `forward`.
void Shift(const float forward[3], float from, float to, float out[3]);

void SetEnabled(bool enabled);

}
