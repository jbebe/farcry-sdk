// The real vehicles the game's cars depict, told apart by their retail mass and engine.
#include "real_vehicle.h"

#include <cmath>

namespace {
    using VehicleOverhaul::RealVehicle::kAtTheAxles;
    using VehicleOverhaul::RealVehicle::Spec;
    using VehicleOverhaul::RealVehicle::Start;

    // The layer's own bank of gear-change clunks, recorded on the Datsun. Set here rather than in the
    // archetypes' sndGearShift fields, which would also clunk every AI car on its made-up gear changes.
    constexpr uint32_t kShiftClunks = 0x00FD0000;

    // A solid axle's springs apart over its wheels apart, the springs under the chassis rails; estimated, not
    // measured. The Rover's leaf springs about 0.8 m apart on a 1.31 m track, the ZIL's about 0.87 m, a usual truck
    // frame, on 1.8 m, and the Unimog's coil springs about 0.8 m on 1.6 m.
    constexpr float kRoverSprings = 0.6f;
    constexpr float kZilSprings = 0.5f;
    constexpr float kUnimogSprings = 0.5f;

    // The shortest suspension, and a truck that climbs on low gearing.
    constexpr Start kRoverTuning[] = {{"Top speed", 1.2f}, {"Springs", 1.0f}};
    // As the user tuned it in game (2026-10-11).
    constexpr Start kDatsunTuning[] = {
        {"Engine power", 1.0f}, {"Shift time", 0.5f}, {"Tyre grip", 0.5f}, {"Rolling resistance", 0.0f}};
    // About a real ZIL-130's pitch and yaw for its weight: the axles' distances from the centre of mass
    // multiplied, 1.9 m each on its 3.8 m wheelbase, against the game's 1 m².
    constexpr Start kZilTuning[] = {{"Inertia", 3.6f}};

    // 2,120 of its 4,300 kg, empty.
    constexpr float kZilFrontWeight = 0.49f;
    constexpr float kZilGears[] = {7.45f, 4.10f, 2.29f, 1.47f, 1.00f};

    constexpr Spec kVehicles[] = {
        {1600.0f, 95.0f, "Land Rover Series III", 0.90f, 0.0f, 0.0f, 0, 0.0f, {kRoverSprings, kRoverSprings},
         kRoverTuning},
        {1000.0f, 90.0f, "Datsun 1200", 1.30f, 750.0f, 6500.0f, kShiftClunks, 0.816f, {}, kDatsunTuning},
        {800.0f, 87.0f, "Buggy", kAtTheAxles},
        {1500.0f, 95.0f, "Jeep Liberty", 1.07f},
        {1200.0f, 88.0f, "Jeep Wrangler", 1.20f},
        {1200.0f, 50.0f, "Jeep Wrangler taxi", 1.20f},
        {600.0f, 80.0f, "Quad", 1.00f},
        {1600.0f, 88.0f, "Unimog", 0.85f, 0.0f, 0.0f, 0, 0.0f, {kUnimogSprings, kUnimogSprings}},
        {4000.0f, 150.0f, "ZIL-130", 0.85f, 560.0f, 3200.0f, kShiftClunks, 0.7f, {kZilSprings, kZilSprings}, kZilTuning,
         kZilFrontWeight, kZilGears, 1300.0f},
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
