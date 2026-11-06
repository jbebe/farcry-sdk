// The player's legs in first person: posed under the hips by the way the player moves, each foot
// set on the ground and the knee bent to reach it, from the full body's own rest pose.
#pragma once

#include <cstdint>

namespace PlayerBody::Legs {

// Once a frame on the game thread, with the seconds since the last; `shown` while the legs are.
void Update(uint8_t* pawn, float seconds, bool shown);

// Puts the legs' pose on the player's skeleton as it is posed, on whichever thread posed it.
void OnPosed(uint8_t* skeleton);

}
