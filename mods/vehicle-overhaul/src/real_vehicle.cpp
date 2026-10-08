// The real vehicles the game's cars depict, told apart by their retail mass and engine.
#include "real_vehicle.h"

#include <cmath>

namespace {
    using VehicleOverhaul::RealVehicle::kAtTheAxles;
    using VehicleOverhaul::RealVehicle::Spec;

    // The layer's own bank of gear-change clunks, recorded on the Datsun. Set here rather than in the
    // archetypes' sndGearShift fields, which would also clunk every AI car on its made-up gear changes.
    constexpr uint32_t kShiftClunks = 0x00FD0000;

    // Leaf springs under the chassis rails, about 0.8 m apart on a 1.31 m track: estimated, not measured.
    constexpr float kRoverSprings = 0.6f;

    constexpr Spec kVehicles[] = {
        // The shortest suspension, and a truck that climbs on low gearing.
        {1600.0f, 95.0f, "Land Rover Series III", 0.90f, 0.0f, 0.0f, 0, {kRoverSprings, kRoverSprings}, 1.2f, 1.0f},
        {1000.0f, 90.0f, "Datsun 1200", 1.30f, 750.0f, 6500.0f, kShiftClunks},
        {800.0f, 87.0f, "Buggy", kAtTheAxles},
        {1500.0f, 95.0f, "Jeep Liberty", 1.07f},
        {1200.0f, 88.0f, "Jeep Wrangler", 1.20f},
        {1200.0f, 50.0f, "Jeep Wrangler taxi", 1.20f},
        {600.0f, 80.0f, "Quad", 1.00f},
        {1600.0f, 88.0f, "Unimog", 0.85f},
        {4000.0f, 150.0f, "ZIL-130", 0.85f, 560.0f, 2700.0f, kShiftClunks},
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

std::span<const Spec> All() { return kVehicles; }

}
