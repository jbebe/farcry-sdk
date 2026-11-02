// Recoil that stays: each shot's kick goes into the aim and is never taken back. The first shots
// of a burst kick lightly, and past its third each shot spreads further.
#pragma once

#include "engine/kick.h"

#include <cstdint>

namespace AimingOverhaul::Recoil {

void SetEnabled(bool enabled);

// Follows the weapon in hand, or null, reading whether the game still takes its kick back each time
// it changes.
void FollowWeapon(uint8_t* weapon);

// Takes the kick of the player's last shot from the game, so it stays in the aim; false where the
// game keeps it.
bool Take(const Kick::Kick& kick);

// Once a frame, with the seconds since the last: turns the look by the kicks as they rise.
void Update(float seconds);

// Bends the direction of a shot about to leave, a unit vector in the world, by the spread its place
// in the burst adds.
void Bend(float direction[3]);

}
