// Solid axles: the two wheels of a beam axle sprung, damped and drawn as one.
#pragma once

#include "engine/wheeled.h"
#include "real_vehicle.h"

namespace VehicleOverhaul::Axles {

// Lets the wheels on `real`'s solid axles hang further than their springs reach. After the tyre solver
// is built, which wants where the springs stop.
void Hang(const Wheeled::Parts& parts, const RealVehicle::Spec& real);

// Rewrites the suspension forces of the wheels on `real`'s solid axles as those of a beam on two
// springs. Physics thread only.
void Spring(float mass, const RealVehicle::Spec& real, const Wheeled::Suspension* wheels, float* forces);

// Leans the drawn wheels of the player's car, `car`, with its solid axles, `real`'s, or straightens them
// for null.
void Lean(Wheeled::Car car, const RealVehicle::Spec* real);

}
