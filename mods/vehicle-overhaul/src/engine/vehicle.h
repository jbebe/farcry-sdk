// The vehicle a pawn sits in.
#pragma once

namespace VehicleOverhaul::Vehicle {

// False, and logged, when this build lacks the seat lookup.
bool Install();

// The CVehicle component of the vehicle `pawn` is seated in, or null on foot. Game thread only.
void* Current(void* pawn);

}
