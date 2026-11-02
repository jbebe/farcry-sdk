// A landing drops the eye as the knees take it, by how fast the player came down, and the view comes
// back up on a spring.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Landing {

// Once a frame on the game thread, after the stance has measured the pawn's velocity.
void Update(uint8_t* pawn, float seconds);

// How far a landing has dropped the eye, in metres.
float EyeDrop();

void SetEnabled(bool enabled);

}
