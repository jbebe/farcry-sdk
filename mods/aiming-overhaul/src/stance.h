// How the player stands: crouched or not, still or moving, which steadies or unsteadies the hands.
// A lean keeps the stance it leans from, and nothing counts as resting the gun.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Stance {

// Once a frame, with the player's pawn and the seconds since the last.
void Update(uint8_t* pawn, float seconds);

// How far crouched and how far moving the player is, each eased from nought to one.
float Crouched();
float Moving();

// The pawn's velocity over the last frame, in metres a second in the world, z up. False where this
// frame measured none: a new pawn, or a frame too long to measure by.
bool Velocity(float out[3]);

// How much of the sway the stance leaves: half crouched and still, all of it standing still, and
// more moving.
float Sway();

}
