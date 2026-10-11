// The real vehicles the game's cars depict, told apart by their retail mass and engine.
#pragma once

#include "engine/wheeled.h"

#include <cstdint>
#include <span>

namespace VehicleOverhaul::RealVehicle {

// A tuning a car starts at, by its key in the tuning file.
struct Start {
    const char* key;
    float value;
};

struct Spec {
    float mass;
    float enginePower;
    // Also its section in the tuning file, so renaming it drops what was tuned for it.
    const char* name;
    // The static stability factor: half the track over the centre of mass's height. kAtTheAxles puts
    // the weight at the wheels' centres instead.
    float stability;
    // The engine's idle and rev limit, or 0 for the drivetrain's defaults. A car with an engine sound of
    // the layer's own has it pitched for these: changing them puts it out of tune.
    float idle;
    float redline;
    // The sound of a gear change, or 0 for the vehicle's own.
    uint32_t shiftSound;
    // Seconds from the driver getting in to the engine catching in the layer's start sound, or 0: the
    // engine sound is silent until then, and fades in after.
    float catchTime;
    // Each solid axle's spring spacing over its track, front then rear, or 0 for an axle whose wheels
    // are sprung apart.
    float solidAxle[Wheeled::kAxles];
    // Where its tunings start, for those that differ from everyone's defaults.
    std::span<const Start> tuning;
    // The share of its weight on the front axle, or 0 to leave it as far forward as the game put it.
    float frontWeight;
};

constexpr float kAtTheAxles = 0.0f;

// The vehicle of All() a car of `mass` and retail `enginePower` depicts, or null for one the overhaul
// does not know.
const Spec* Match(float mass, float enginePower);

// Every vehicle the overhaul knows.
std::span<const Spec> All();

}
