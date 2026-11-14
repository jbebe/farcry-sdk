// The player's shadow in first person on foot, where the engine casts none: a soft circle on the
// ground under the feet, drawn over the world from its depth.
#pragma once

#include <cstdint>

namespace ThirdPerson::Shadow {

// Hooks the renderer's view update and the engine device's EndScene. False, and logged, when they
// cannot be hooked.
bool Install();

void SetEnabled(bool enabled);

// Once a frame on the game thread: where the feet stand.
void Update(uint8_t* pawn);

}
