// CCameraThirdComponent's update, with the camera's placement handed to the plugin. See
// docs/docs/engine-internals/free-camera-and-noclip.md.
#pragma once

#include <cstdint>

namespace PlayerBody::ThirdCamera {

// A world position, and angles in radians: pitch (up positive), roll, yaw (0 faces +Y).
struct Pose {
    float position[3];
    float angles[3];
};

// Called from the update with the component. True places the camera at `pose` instead of where the
// engine would.
using PoseFn = bool (*)(uint8_t* camera, Pose& pose);

// False, and logged, when this build lacks anything the placement needs.
bool Install(PoseFn pose);

}
