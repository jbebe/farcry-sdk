// The camera the local player sees through. See docs/docs/engine-internals/free-camera-and-noclip.md.
#pragma once

#include <cstdint>

namespace ThirdPerson::Camera {

// Entity-library archetype names from game data, not strings in Dunia.dll.
inline constexpr const char* kFirst = "Cameras.Camera.First";
inline constexpr const char* kThird = "Cameras.Camera.Third";

// False, and logged, when this build lacks the camera manager's entry points.
bool Install();

// The camera the player sees through, or null.
uint8_t* Active();

// Where the camera the player sees through stands in the world; false where there is none.
bool Place(float out[3]);

// Puts up the camera archetype `name` and returns it; null when it did not take, as while a
// cutscene owns the camera. Game thread.
uint8_t* Activate(const char* name);

}
