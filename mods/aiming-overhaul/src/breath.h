// Holding the breath while aiming a gun that needs it: sprint held down its scope steadies the sway
// for a few seconds, then the hands shake harder until it is let go, and the player is winded for a
// while after.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Breath {

// Follows the sprint signals.
void OnSignal(uint32_t signal);

// Follows the weapon in hand, or null, reading whether its archetype's entity data asks for the
// breath to be held each time it changes.
void FollowWeapon(uint8_t* weapon);

// Whether the weapon in hand's breath can be held, its scope swaying harder for it.
bool Needed();

// Once a frame, with whether the player is down the sights or a scope and how much stamina they
// have left, from nought to one, which decides how hard the breath is drawn and let out.
void Update(float seconds, bool aiming, float stamina);

// How much of the sway is left: below one while held, above it once the breath runs out or after.
float Steadiness();

// Whether sprint, pressed down the sights, holds the breath instead of sprinting: with the hold on,
// and the weapon in hand needing it.
bool HoldsSprint();

void SetEnabled(bool enabled);

}
