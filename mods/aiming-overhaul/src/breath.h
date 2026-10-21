// Holding the breath while aiming: sprint held down the sights steadies the sway for a few seconds,
// then the hands shake harder until it is let go, and the player is winded for a while after.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Breath {

// Follows the sprint signals.
void OnSignal(uint32_t signal);

// Once a frame, with whether the player is down the sights or a scope.
void Update(float seconds, bool aiming);

// How much of the sway is left: below one while held, above it once the breath runs out or after.
float Steadiness();

// Whether sprint, pressed down the sights, holds the breath instead of sprinting.
bool HoldsSprint();

void SetEnabled(bool enabled);

}
