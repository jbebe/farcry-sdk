// The chase camera: behind the vehicle, swinging after its heading, orbited by the mouse.
#pragma once

#include "engine/third_camera.h"

namespace VehicleOverhaul::Chase {

// Starts the next placement straight behind the vehicle.
void Reset();

// This frame's mouse look, in the listener's units: half a turn a second each.
void Look(float pitch, float yaw);

// The camera for one frame, around the vehicle with world transform `vehicle`.
ThirdCamera::Pose Place(const float* vehicle, float seconds);

}
