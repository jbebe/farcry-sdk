// The real vehicles the game's cars depict, named by their archetypes' entity data.
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
    // Also its section in the tuning file and the name its archetypes' entity data gives, so renaming it
    // drops what was tuned for it and needs the layer's fragments rewritten.
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
    // Its gearbox's forward ratios, first to top, and the revs it changes down at; empty for Havok's.
    std::span<const float> gears;
    float downshift;
};

constexpr float kAtTheAxles = 0.0f;

// Names the vehicle the player's `car` depicts from its `entity`'s data, on the game thread; both are
// null while the player drives nothing.
void Follow(Wheeled::Car car, void* entity);

// The vehicle the player's `car` depicts, or null for one the overhaul does not know or has yet to name.
// Any thread.
const Spec* OfPlayer(Wheeled::Car car);

// Every vehicle the overhaul knows.
std::span<const Spec> All();

}
