// How the player stands: crouched or not, still or moving, which steadies or unsteadies the hands.
// A lean keeps the stance it leans from.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Stance {

// Once a frame, with the player's pawn and the seconds since the last.
void Update(uint8_t* pawn, float seconds);

// How far crouched and how far moving the player is, each eased from nought to one.
float Crouched();
float Moving();

// The sway the stance leaves, given the shares left standing and crouched: `standing` standing
// still, half of `crouched` crouched still, `standing` crouched moving, and twice `standing`
// standing moving.
float Sway(float standing, float crouched);

}
