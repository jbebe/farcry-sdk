// Recoil that stays: each shot's kick goes into the aim and is never taken back. The first shots
// of a burst kick lightly, and past its third each shot spreads further and, instead of climbing,
// the arms point the gun off at random, the shots with it.
#pragma once

#include "body.h"
#include "engine/kick.h"

#include <cstdint>

namespace AimingOverhaul::Recoil {

void SetEnabled(bool enabled);

// Reads whether the game still takes the kick back for the weapon now in hand, or null. Call each
// time the weapon changes.
void FollowWeapon(uint8_t* weapon);

// Takes the kick of the player's last shot from the game, so it stays in the aim; false where the
// game keeps it.
bool Take(const Kick::Kick& kick);

// Once a frame, with the seconds since the last: turns the look by the kicks as they rise, and
// shakes the arms through a burst.
void Update(float seconds);

// How far a burst's shake turns the arms this frame.
Body::Offset Posed();

// Bends the direction of a shot about to leave, a unit vector in the world, by where the arms point
// the gun and the spread its place in the burst adds.
void Bend(float direction[3]);

}
