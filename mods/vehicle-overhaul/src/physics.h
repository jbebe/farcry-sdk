// The off-road physics: the car the player drives, re-tuned every physics step while they drive it
// and put back as the game made it when they get out.
#pragma once

#include "engine/wheeled.h"
#include "real_vehicle.h"

namespace VehicleOverhaul::Physics {

// False, and logged, when this build lacks the vehicle physics.
bool Install();

// What the player's car is doing and how it stands, for the window.
struct Status {
    // With the engine's revs from the drivetrain in place of the wheels'.
    Wheeled::Readout motion;
    // The real vehicle it was matched to, or null for a car the overhaul does not know.
    const RealVehicle::Spec* real;
    // The centre of mass above the ground under the wheels, in metres.
    float height;
    // Half the track over that height: the sideways g at which it tips over.
    float stability;
    // The sideways g the tyres can hold on what they stand on.
    float grip;
};

// False when the player is not driving.
bool Latest(Status& status);

}
