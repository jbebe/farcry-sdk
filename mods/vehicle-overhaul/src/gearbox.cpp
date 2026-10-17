// The gear changes of the car the player drives, made as Far Cry 3 makes them in place of Havok's.
#include "gearbox.h"

#include <algorithm>
#include <cmath>

namespace {
    // The share the next gear's revs must clear the downshift by, for the slowing the clutch time misses.
    constexpr float kUpshiftMargin = 0.1f;
    // A change down this many seconds after the clutch of a change up came in undoes it: the car could not
    // pull the higher gear.
    constexpr float kUndoneWithin = 1.5f;
    // How far the road must ease, in m/s² of gravity along it, before an undone change up is tried again.
    constexpr float kEased = 0.5f;

    // The car's last change up, and the gear an undone one came back to with the slope it came back on.
    struct Memory {
        const void* vehicle = nullptr;
        float sinceUpshift = 1e9f;
        int undoneGear = -1;
        float undoneSlope = 0.0f;
    };
    Memory g_memory;

    void Change(const VehicleOverhaul::Wheeled::Parts& parts, int gear, float clutchTime) {
        *parts.gear = static_cast<int8_t>(gear);
        *parts.clutchOut = 1;
        *parts.clutchLeft = clutchTime;
    }
}

namespace VehicleOverhaul::Gearbox {

void Step(const Wheeled::Parts& parts, const Wheeled::Readout& readout, const Shifts& shifts, float rolling,
          bool grounded, float seconds) {
    if (parts.vehicle != g_memory.vehicle) {
        g_memory = {parts.vehicle};
    }
    g_memory.sinceUpshift += seconds;
    if (*parts.clutchOut != 0) {
        return;
    }
    // A car stopped in a higher gear, or held there by reversing, would pull away in it and change down on
    // the move.
    if (*parts.reversing != 0 || readout.speed < kStanding) {
        *parts.gear = 0;
        g_memory.undoneGear = -1;
        return;
    }
    const float rpm = std::abs(readout.rpm);
    const int gear = *parts.gear;
    if (rpm < shifts.downshift && gear > 0) {
        if (g_memory.sinceUpshift < shifts.upshiftTime + kUndoneWithin) {
            g_memory.undoneGear = gear - 1;
            g_memory.undoneSlope = readout.gravityAlong;
        }
        Change(parts, gear - 1, shifts.downshiftTime);
        return;
    }
    if (rpm <= shifts.upshift || gear + 1 >= *parts.gearCount || !grounded ||
        (gear == g_memory.undoneGear && readout.gravityAlong < g_memory.undoneSlope + kEased)) {
        return;
    }
    const float slowed = (readout.gravityAlong - rolling) * shifts.upshiftTime;
    const float next =
        rpm * parts.gears[gear + 1] / parts.gears[gear] * (std::max)(0.0f, 1.0f + slowed / readout.speed);
    if (next > shifts.downshift * (1.0f + kUpshiftMargin)) {
        g_memory.sinceUpshift = 0.0f;
        g_memory.undoneGear = -1;
        Change(parts, gear + 1, shifts.upshiftTime);
    }
}

}
