// A vehicle's engine sound and rev counter (CVehicleTypeWheeled), with the gear changes and the RPM
// they follow handed to the plugin for the vehicles it drives.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::VehicleSound {

struct Engine {
    // The gear change to sound this frame: +1 up, -1 down, 0 none.
    int shift;
    float rpm;
    // The sound a gear change plays, or 0 for the vehicle's own.
    uint32_t shiftSound;
};

// For the vehicle entity a sound belongs to: false leaves it to the game; true sets its engine.
using EngineFn = bool (*)(void* vehicle, Engine& engine);

// False, and logged, when this build lacks the update's gear and RPM steps.
bool Install(EngineFn engine);

}
