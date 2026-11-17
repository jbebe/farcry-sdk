// Each hit on the player, as the engine deals it.
#pragma once

namespace AimingOverhaul::Damage {

// A bullet, blast or blade that took `lost` of the player's health, from nought to one, and whether
// it landed on the head. On the game thread.
using HitFn = void (*)(bool head, float lost);

// The player's health after any damage, from nought to one. On the game thread.
using HealthFn = void (*)(float health);

// False, and logged, when the player's damage cannot be hooked.
bool Install(HitFn onHit, HealthFn onHealth);

}
