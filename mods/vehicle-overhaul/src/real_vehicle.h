// The real vehicles the game's cars depict, told apart by their retail mass and engine.
#pragma once

#include "engine/wheeled.h"

#include <cstdint>
#include <span>

namespace VehicleOverhaul::RealVehicle {

struct Spec {
    float mass;
    float enginePower;
    // Also its section in the tuning file, so renaming it drops what was tuned for it.
    const char* name;
    // The static stability factor: half the track over the centre of mass's height. kAtTheAxles puts
    // the weight at the wheels' centres instead.
    float stability;
    // The engine's idle and rev limit, or 0 for the drivetrain's defaults.
    float idle;
    float redline;
    // The sound of a gear change, or 0 for the vehicle's own.
    uint32_t shiftSound;
    // Each solid axle's spring spacing over its track, front then rear, or 0 for an axle whose wheels
    // are sprung apart.
    float solidAxle[Wheeled::kAxles];
    // Where its Top speed and Springs tunings start, or 0 for the defaults, which were found on the Datsun.
    float topSpeed;
    float springs;
};

constexpr float kAtTheAxles = 0.0f;

// The vehicle of All() a car of `mass` and retail `enginePower` depicts, or null for one the overhaul
// does not know.
const Spec* Match(float mass, float enginePower);

// Every vehicle the overhaul knows.
std::span<const Spec> All();

}
