// The player seen over the right shoulder on foot, through the engine's own third-person camera.
#pragma once

#include <cstdint>

namespace PlayerBody::ThirdPerson {

// Hooks the camera's placement. False, and logged, when this build lacks it.
bool Install();

void SetEnabled(bool enabled);

// Once a frame on the game thread, before anything that reads the view mode.
void Update(uint8_t* pawn);

}
