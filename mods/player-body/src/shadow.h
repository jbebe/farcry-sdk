// The player's shadow in first person on foot, where the engine casts none: a soft blur cast along
// the sun from a column standing in for the body, drawn over the world from its depth.
#pragma once

#include <cstdint>

namespace PlayerBody::Shadow {

// Hooks the sun and the frame. False, and logged, when this build lacks either.
bool Install();

void SetEnabled(bool enabled);

// Once a frame on the game thread: where the body stands.
void Update(uint8_t* pawn);

}
