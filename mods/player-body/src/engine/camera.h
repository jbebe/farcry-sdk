// The camera the local player sees through. See docs/docs/engine-internals/free-camera-and-noclip.md.
#pragma once

namespace PlayerBody::Camera {

// False, and logged, when this build lacks the camera manager's entry points.
bool Install();

// Where the camera the player sees through stands in the world; false where there is none.
bool Place(float out[3]);

}
