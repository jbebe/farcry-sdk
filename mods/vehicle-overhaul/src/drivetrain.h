// The engine of the car the player drives, as its sound and rev counter hear it: revs from the real
// gearbox, and its gear changes.
#pragma once

#include "engine/wheeled.h"
#include "real_vehicle.h"

namespace VehicleOverhaul::Drivetrain {

// False, and logged, when this build lacks the sound's gear and RPM steps, the input pass or the
// driven vehicle.
bool Install();

// Moves the engine on by one physics step of the player's car, the vehicle `real` depicts or null,
// whose gear changes keep the clutch out for `shiftTime` seconds. The engine's revs after it.
float Step(Wheeled::Car car, const RealVehicle::Spec* real, float shiftTime, const Wheeled::Readout& readout,
           float seconds);

}
