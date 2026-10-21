// Leaning: held Q or E rolls the body out to the side about its feet, eye, gun and all.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Lean {

// Radians at full lean, which takes the eye about 0.33 m to the side.
inline constexpr float kMostRoll = 0.21f;

// Follows the lean signals.
void OnSignal(uint32_t signal);

// The body's roll this frame in radians, right positive, given the seconds since the last, whether
// the player may lean now, and the roll to lean to while no lean key is held.
float Roll(float seconds, bool free, float autoRoll);

// The roll Roll last gave, and the one it was heading to.
float Current();
float Target();

// How the eye moves, in the world, when the body rolls from `from` to `to` radians facing
// `forward`.
void Shift(const float forward[3], float from, float to, float out[3]);

void SetEnabled(bool enabled);

}
