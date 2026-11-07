// The player seen from outside through the engine's own third-person camera, switched by the
// Third-person view control: over the right shoulder on foot, with the game's own crosshair, shots
// aimed at it and no iron sights, and a chase camera behind the car at the wheel.
#pragma once

#include <cstdint>

namespace ThirdPerson::Outside {

// Hooks the camera's placement, the control, the crosshair and the shot. False, and logged, when
// this build lacks the placement or the control's signal.
bool Install();

// Once a frame on the game thread, before anything that reads the view mode. `look` is the frame's
// mouse look, which the chase camera takes.
void Update(uint8_t* pawn, float* look);

}
