// The gear changes of the car the player drives, made as Far Cry 3 makes them in place of Havok's.
#include "gearbox.h"

#include <algorithm>
#include <cmath>

namespace {
    // Below this speed a change up is not weighed: the revs come from spinning wheels.
    constexpr float kMinUpshiftSpeed = 1.0f;

    void Change(const VehicleOverhaul::Wheeled::Parts& parts, int gear, float clutchTime) {
        *parts.gear = static_cast<int8_t>(gear);
        *parts.clutchOut = 1;
        *parts.clutchLeft = clutchTime;
    }
}

namespace VehicleOverhaul::Gearbox {

void Step(const Wheeled::Parts& parts, const Wheeled::Readout& readout, const Shifts& shifts, float rolling,
          bool grounded) {
    if (*parts.clutchOut != 0 || *parts.reversing != 0) {
        return;
    }
    const float rpm = std::abs(readout.rpm);
    const int gear = *parts.gear;
    if (rpm < shifts.downshift && gear > 0) {
        Change(parts, gear - 1, shifts.downshiftTime);
        return;
    }
    if (rpm <= shifts.upshift || gear + 1 >= *parts.gearCount || !grounded || readout.speed < kMinUpshiftSpeed) {
        return;
    }
    const float slowed = (readout.gravityAlong - rolling) * shifts.upshiftTime;
    const float next =
        rpm * parts.gears[gear + 1] / parts.gears[gear] * (std::max)(0.0f, 1.0f + slowed / readout.speed);
    if (next > shifts.downshift) {
        Change(parts, gear + 1, shifts.upshiftTime);
    }
}

}
