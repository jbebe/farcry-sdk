// The gear changes of the car the player drives, made as Far Cry 3 makes them in place of Havok's, which
// also change gear while the clutch is out.
#pragma once

#include "engine/wheeled.h"

namespace VehicleOverhaul::Gearbox {

// Where the car changes gear, in Havok's revs, and how long a change up and down has the clutch out.
struct Shifts {
    float upshift;
    float downshift;
    float upshiftTime;
    float downshiftTime;
};

// Changes gear for one physics step: never while the clutch is out or in reverse, down below the
// downshift, and up past the upshift only if the revs in the next gear stay above the downshift once the
// slope and `rolling` drag, in m/s², have slowed the car for the clutch time.
void Step(const Wheeled::Parts& parts, const Wheeled::Readout& readout, const Shifts& shifts, float rolling,
          bool grounded);

}
