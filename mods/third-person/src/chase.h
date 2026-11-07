// The chase camera: behind the car, swinging after its heading, orbited by the mouse.
#pragma once

#include "engine/third_camera.h"

namespace ThirdPerson::Chase {

// Starts the next placement straight behind the car.
void Reset();

// This frame's mouse look, in the listener's units: half a turn a second each.
void Look(float pitch, float yaw);

// The camera for one frame, around the car with world transform `car`.
ThirdCamera::Pose Place(const float* car, float seconds);

}
