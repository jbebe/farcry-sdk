// The real vehicles the game's cars depict, told apart by their retail mass and engine.
#include "real_vehicle.h"

#include <cmath>

namespace {
    using VehicleOverhaul::RealVehicle::kAtTheAxles;
    using VehicleOverhaul::RealVehicle::Spec;

    // The layer's own bank of gear-change clunks. Set here rather than in the archetypes' sndGearShift
    // fields, which would also clunk every AI Datsun on its made-up gear changes.
    constexpr uint32_t kDatsunShift = 0x00FD0000;

    constexpr Spec kVehicles[] = {
        {1600.0f, 95.0f, "Land Rover Series III", 0.90f},
        // Its engine sound bank is pitched for this idle and redline: changing them puts it out of tune.
        {1000.0f, 90.0f, "Datsun 1200", 1.30f, 750.0f, 6500.0f, kDatsunShift},
        {800.0f, 87.0f, "Buggy", kAtTheAxles},
        {1500.0f, 95.0f, "Jeep Liberty", 1.07f},
        {1200.0f, 88.0f, "Jeep Wrangler", 1.20f},
        {1200.0f, 50.0f, "Jeep Wrangler taxi", 1.20f},
        {600.0f, 80.0f, "Quad", 1.00f},
        {1600.0f, 88.0f, "Unimog", 0.85f},
        {4000.0f, 150.0f, "ZIL-130", 0.85f},
    };
}

namespace VehicleOverhaul::RealVehicle {

const Spec* Match(float mass, float enginePower) {
    for (const Spec& spec : kVehicles) {
        if (std::abs(spec.mass - mass) < 1.0f && std::abs(spec.enginePower - enginePower) < 0.5f) {
            return &spec;
        }
    }
    return nullptr;
}

}
