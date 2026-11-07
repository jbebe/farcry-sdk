// The player seen over the right shoulder on foot, through the engine's own third-person camera,
// toggled on a key, with the game's own crosshair, shots aimed at it, and no iron sights.
#pragma once

#include <cstdint>

namespace PlayerBody::ThirdPerson {

// The keys the view can be toggled on, as the setting's choices; the first is Off.
inline constexpr const char* kKeyLabels[] = {"Off", "V", "N", "F3", "F4", "F6", "F7", "F8"};

// Hooks the camera's placement, the crosshair and the shot. False, and logged, when this build lacks
// the placement.
bool Install();

void SetKey(uint32_t choice);

// Once a frame on the game thread, before anything that reads the view mode.
void Update(uint8_t* pawn);

}
