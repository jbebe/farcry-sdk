// A vehicle's engine sound and rev counter (CVehicleTypeWheeled), with the gear changes and the RPM
// they follow handed to the plugin for the vehicles it drives.
#pragma once

namespace VehicleOverhaul::VehicleSound {

// For the vehicle entity a sound belongs to: false leaves it to the game; true sets the gear change to
// sound this frame (+1 up, -1 down, 0 none) and the RPM.
using EngineFn = bool (*)(void* vehicle, int& shift, float& rpm);

// False, and logged, when this build lacks the update's gear and RPM steps.
bool Install(EngineFn engine);

}
