// CCameraThirdComponent's update, with the camera's placement handed to the plugin.
#pragma once

namespace VehicleOverhaul::ThirdCamera {

// A world position, and angles in radians: pitch (up positive), roll, yaw (0 faces +Y).
struct Pose {
    float position[3];
    float angles[3];
};

// Called from the update with the component and the frame time. True places the camera at `pose`
// instead of where the engine would.
using PoseFn = bool (*)(void* camera, float seconds, Pose& pose);

// False, and logged, when this build lacks anything the placement needs.
bool Install(PoseFn pose);

}
