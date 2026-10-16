// The vehicle a pawn sits in.
#pragma once

namespace VehicleOverhaul::Vehicle {

// False, and logged, when this build lacks the seat lookup.
bool Install();

// The CVehicle component of the vehicle `pawn` is seated in, or null on foot. Game thread only.
void* Current(void* pawn);

// The vehicle's archetype iAnimVehicleType, one per kind of car (the Datsun's 10, the Rover's 1), or -1
// for no vehicle. Game thread only.
int AnimType(void* vehicle);

}
