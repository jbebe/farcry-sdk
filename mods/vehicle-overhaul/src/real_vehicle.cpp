// The real vehicles the game's cars depict, named by their archetypes' entity data.
#include "real_vehicle.h"

#include "fcse_api.h"

#include <atomic>
#include <cstring>

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
    // As the user tuned it in game (2026-10-12).
    constexpr Start kDatsunTuning[] = {
        {"Engine power", 0.8f},      {"Shift time", 0.5f},        {"Downshift time", 0.5f},
        {"Steering at speed", 1.0f}, {"Tyre grip", 0.4f},         {"Rolling resistance", 0.0f},
        {"Inertia", 1.02f},          {"Springs", 0.55f},          {"Compression damping", 0.48f},
        {"Rebound damping", 0.4f}};
    // A real empty ZIL-130 (2026-10-12): 90 km/h through its own gearbox on the truck's 0.53 m wheels; a truck
    // driver's gear changes; air brakes; 30 degrees of lock; 2% rolling drag; its pitch and yaw for its 3.8 m
    // wheelbase; leaf springs at 2 Hz on about 15 cm of travel. Its 402 N m (1.3) and the ground's own grip (0.33)
    // spun the wheels, so the engine and grip are as the user set them.
    constexpr Start kZilTuning[] = {
        {"Engine power", 1.0f},      {"Top speed", 1.55f},         {"Shift time", 1.0f},
        {"Downshift time", 1.0f},    {"Brake torque", 0.75f},      {"Steering lock", 0.85f},
        {"Steering at speed", 1.0f}, {"Tyre grip", 0.6f},          {"Rolling resistance", 0.2f},
        {"Inertia", 3.6f},           {"Springs", 0.8f},            {"Travel", 1.5f},
        {"Compression damping", 0.65f}, {"Rebound damping", 0.65f}};

    // A real Unimog 406 (2026-10-12), as the ZIL-130 was worked out: its OM 352's 235 N m at 71 km/h on the game's
    // 0.54 m wheels, scaled to the game's 1,600 kg for the real 3,900; synchromesh gear changes; air brakes; about
    // 28 degrees of lock; its pitch and yaw for its 3 m wheelbase; coil springs at 1.5 Hz on long travel.
    constexpr Start kUnimogTuning[] = {
        {"Engine power", 0.5f},      {"Top speed", 1.65f},         {"Shift time", 0.8f},
        {"Downshift time", 0.8f},    {"Brake torque", 0.45f},      {"Steering lock", 0.95f},
        {"Steering at speed", 1.0f}, {"Tyre grip", 0.6f},          {"Rolling resistance", 0.2f},
        {"Inertia", 2.0f},           {"Springs", 0.65f},           {"Travel", 2.0f},
        {"Compression damping", 0.5f}, {"Rebound damping", 0.5f}};

    // 2,120 of its 4,300 kg, empty.
    constexpr float kZilFrontWeight = 0.49f;
    constexpr float kZilGears[] = {7.45f, 4.10f, 2.29f, 1.47f, 1.00f};

    constexpr Spec kVehicles[] = {
        {"Land Rover Series III", 0.90f, 0.0f, 0.0f, 0, 0.0f, {kRoverSprings, kRoverSprings},
         kRoverTuning},
        // 54% on the front axle, a small front-engined car's; the game's put it so far forward that the rear hopped
        // about (2026-10-12).
        {"Datsun 1200", 1.30f, 750.0f, 6500.0f, kShiftClunks, 0.816f, {}, kDatsunTuning, 0.54f},
        {"Buggy", kAtTheAxles},
        {"Jeep Liberty", 1.07f},
        {"Jeep Wrangler", 1.20f},
        {"Jeep Wrangler taxi", 1.20f},
        {"Quad", 1.00f},
        // An OM 352 diesel's idle and governed top, and its start take's catch, estimated from its loudness. It tips at
        // 1.1 g as the big truck does, on the same soft solid axles. The game's 60% on the front axle is a Unimog's.
        {"Unimog", 1.1f, 600.0f, 2800.0f, kShiftClunks, 0.4f, {kUnimogSprings, kUnimogSprings},
         kUnimogTuning},
        // Above the real truck's 0.85, at which it rolled over in left-right turns (2026-10-12).
        {"ZIL-130", 1.1f, 560.0f, 3200.0f, kShiftClunks, 0.7f, {kZilSprings, kZilSprings}, kZilTuning,
         kZilFrontWeight, kZilGears, 1300.0f},
    };

    // The entity data key naming the vehicle a car's archetype depicts, which the layer's vehicle fragments set.
    constexpr const char* kKey = "VehicleOverhaul.Vehicle";

    struct Followed {
        VehicleOverhaul::Wheeled::Car car;
        const Spec* spec;
    };
    std::atomic<Followed> g_player{};
    // The entity Follow last named, on the game thread.
    void* g_entity = nullptr;

    const Spec* Named(void* entity) {
        char name[64];
        if (!FCSE::ApiPointer()->EntityData->GetString(entity, kKey, name, sizeof(name))) {
            FCSE::Logf("vehicles: this car has no %s data, so the overhaul does not know it", kKey);
            return nullptr;
        }
        for (const Spec& spec : kVehicles) {
            if (std::strcmp(spec.name, name) == 0) {
                return &spec;
            }
        }
        FCSE::Logf("vehicles: %s names \"%s\", which the overhaul does not know", kKey, name);
        return nullptr;
    }
}

namespace VehicleOverhaul::RealVehicle {

void Follow(Wheeled::Car car, void* entity) {
    if (entity == g_entity) {
        return;
    }
    g_entity = entity;
    g_player = {car, entity != nullptr ? Named(entity) : nullptr};
}

const Spec* OfPlayer(Wheeled::Car car) {
    const Followed followed = g_player;
    return followed.car == car ? followed.spec : nullptr;
}

std::span<const Spec> All() { return kVehicles; }

}
