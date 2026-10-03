// The real vehicles the game's cars depict, told apart by their retail mass and engine.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::RealVehicle {

struct Spec {
    float mass;
    float enginePower;
    const char* name;
    // The static stability factor: half the track over the centre of mass's height. kAtTheAxles puts
    // the weight at the wheels' centres instead.
    float stability;
    // The engine's idle and rev limit, or 0 for the drivetrain's defaults.
    float idle;
    float redline;
    // The sound of a gear change, or 0 for the vehicle's own.
    uint32_t shiftSound;
};

constexpr float kAtTheAxles = 0.0f;

// The vehicle a car of `mass` and retail `enginePower` depicts, or null for one the overhaul does not
// know.
const Spec* Match(float mass, float enginePower);

}
