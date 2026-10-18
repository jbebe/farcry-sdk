// The real vehicles the game's cars depict, told apart by their archetypes' iAnimVehicleType.
#include "real_vehicle.h"

#include "engine/vehicle.h"

#include "fcse_api.h"

#include <atomic>

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

    // Each as the user tuned it in game (2026-10-18).
    constexpr Start kRoverTuning[] = {
        {"Engine power", 0.9f},      {"Top speed", 1.2f},           {"Downshift time", 0.5f},
        {"Steering lock", 1.3f},     {"Steering at speed", 1.0f},   {"Inertia", 2.0f},
        {"Springs", 1.0f},           {"Travel", 0.8f},              {"Compression damping", 0.5f},
        {"Rebound damping", 0.5f}};
    constexpr Start kBuggyTuning[] = {
        {"Engine power", 0.9f},      {"Top speed", 2.5f},           {"Shift time", 0.5f},
        {"Downshift time", 0.3f},    {"Steering lock", 0.9f},       {"Steering at speed", 1.0f},
        {"Tyre grip", 0.5f},         {"Inertia", 1.4f}};
    constexpr Start kWranglerTuning[] = {
        {"Engine power", 1.4f},      {"Shift time", 0.5f},          {"Downshift time", 0.3f},
        {"Brake torque", 0.35f},     {"Steering lock", 1.35f},      {"Steering at speed", 0.7f},
        {"Tyre grip", 0.7f},         {"Inertia", 2.0f},             {"Springs", 0.55f},
        {"Travel", 1.2f},            {"Compression damping", 0.5f}, {"Rebound damping", 0.5f}};
    constexpr Start kQuadTuning[] = {
        {"Engine power", 0.6f},      {"Top speed", 1.7f},           {"Shift time", 0.3f},
        {"Downshift time", 0.2f},    {"Steering lock", 1.1f},       {"Steering at speed", 1.2f},
        {"Tyre grip", 0.35f},        {"Rolling resistance", 0.3f},  {"Inertia", 1.3f},
        {"Travel", 1.7f},            {"Rebound damping", 0.6f}};
    constexpr Start kDatsunTuning[] = {
        {"Engine power", 0.8f},      {"Shift time", 0.5f},        {"Downshift time", 0.5f},
        {"Steering at speed", 0.7f}, {"Tyre grip", 0.4f},         {"Rolling resistance", 0.0f},
        {"Inertia", 1.02f},          {"Springs", 0.55f},          {"Compression damping", 0.48f},
        {"Rebound damping", 0.4f}};
    // Worked out from a real empty ZIL-130 (2026-10-12): 90 km/h through its own gearbox, leaf springs at 2 Hz on
    // about 15 cm of travel; then tuned by the user in game (2026-10-18).
    constexpr Start kZilTuning[] = {
        {"Top speed", 1.55f},        {"Downshift time", 0.3f},     {"Brake torque", 0.75f},
        {"Steering lock", 1.2f},     {"Steering at speed", 0.85f}, {"Tyre grip", 0.6f},
        {"Rolling resistance", 0.2f}, {"Inertia", 3.6f},           {"Springs", 0.8f},
        {"Travel", 1.5f},            {"Compression damping", 0.65f}, {"Rebound damping", 0.55f}};

    // A real Unimog 406 (2026-10-12), as the ZIL-130 was worked out: its OM 352's 235 N m at 71 km/h on the game's
    // 0.54 m wheels, scaled to the game's 1,600 kg for the real 3,900; synchromesh gear changes; air brakes; about
    // 28 degrees of lock; its pitch and yaw for its 3 m wheelbase; coil springs at 1.5 Hz on long travel.
    constexpr Start kUnimogTuning[] = {
        {"Engine power", 0.5f},      {"Top speed", 1.65f},         {"Shift time", 0.8f},
        {"Downshift time", 0.8f},    {"Brake torque", 0.45f},      {"Steering lock", 0.95f},
        {"Steering at speed", 1.0f}, {"Tyre grip", 0.6f},          {"Rolling resistance", 0.2f},
        {"Inertia", 2.0f},           {"Springs", 0.65f},           {"Travel", 2.0f},
        {"Compression damping", 0.5f}, {"Rebound damping", 0.5f}};

    // An automatic, its shift times 0.
    constexpr Start kLibertyTuning[] = {
        {"Engine power", 1.4f},      {"Shift time", 0.0f},          {"Downshift time", 0.0f},
        {"Brake torque", 0.4f},      {"Steering lock", 1.3f},       {"Tyre grip", 0.35f},
        {"Inertia", 1.8f},           {"Springs", 0.5f},             {"Travel", 1.2f},
        {"Compression damping", 0.5f}, {"Rebound damping", 0.4f}};

    // 2,120 of its 4,300 kg, empty.
    constexpr float kZilFrontWeight = 0.49f;
    constexpr float kZilGears[] = {7.45f, 4.10f, 2.29f, 1.47f, 1.00f};

    constexpr Spec kVehicles[] = {
        // Its four's low idle and its start take's catch, from the takes, and a redline of 4,000, where an old 2.25 makes
        // its power: the engine is spared revs past it (2026-10-17). It tips at 1.1 g as the big truck and the Unimog
        // do: at 0.9 it rocked heavily on its soft solid axles, whatever the damping (2026-10-18).
        {"Land Rover Series III", 1, 1.1f, 525.0f, 4000.0f, 0, 0.8f, {kRoverSprings, kRoverSprings},
         kRoverTuning},
        // 54% on the front axle, a small front-engined car's; the game's put it so far forward that the rear hopped
        // about (2026-10-12).
        {"Datsun 1200", 10, 1.30f, 750.0f, 6500.0f, kShiftClunks, 0.816f, {}, kDatsunTuning, 0.54f},
        // Its four's idle and top, and its start take's catch, from the takes (2026-10-17).
        {"Buggy", 3, kAtTheAxles, 950.0f, 4800.0f, 0, 0.5f, {}, kBuggyTuning},
        // Its takes are a 2013 Volvo V70's five-cylinder diesel: its idle, its start take's catch and its cut-off
        // (2026-10-18). 57% on the front axle, a front-engined SUV's, where the game's put 63%.
        {"Jeep Liberty", 6, 1.07f, 690.0f, 4800.0f, 0, 0.9f, {}, kLibertyTuning, 0.57f},
        // Its takes are a 1999 Ram 2500's 5.9 Cummins six: its idle and start take's catch, and the diesel's governed
        // top (2026-10-18). 55% on the front axle, a front-engined Wrangler's: the game's left 59% on the rear, and the
        // car unstable.
        {"Jeep Wrangler", 9, 1.20f, 750.0f, 3000.0f, 0, 0.6f, {}, kWranglerTuning, 0.55f},
        // Its four-stroke's idle and top, and its start take's catch, from the takes (2026-10-17).
        {"Quad", 5, 1.00f, 1580.0f, 9300.0f, 0, 0.5f, {}, kQuadTuning},
        // An OM 352 diesel's idle and governed top, and its start take's catch, estimated from its loudness. It tips at
        // 1.1 g as the big truck does, on the same soft solid axles. The game's 60% on the front axle is a Unimog's.
        {"Unimog", 12, 1.1f, 600.0f, 2800.0f, kShiftClunks, 0.4f, {kUnimogSprings, kUnimogSprings},
         kUnimogTuning},
        // Above the real truck's 0.85, at which it rolled over in left-right turns (2026-10-12).
        {"ZIL-130", 8, 1.1f, 560.0f, 3200.0f, kShiftClunks, 0.7f, {kZilSprings, kZilSprings}, kZilTuning,
         kZilFrontWeight, kZilGears, 1300.0f},
    };

    struct Followed {
        VehicleOverhaul::Wheeled::Car car;
        const Spec* spec;
    };
    std::atomic<Followed> g_player{};
    // The CVehicle Follow last named, on the game thread.
    void* g_vehicle = nullptr;

    const Spec* OfType(int animType) {
        for (const Spec& spec : kVehicles) {
            if (spec.animType == animType) {
                return &spec;
            }
        }
        FCSE::Logf("vehicles: a car of iAnimVehicleType %d is not one the overhaul knows", animType);
        return nullptr;
    }
}

namespace VehicleOverhaul::RealVehicle {

void Follow(Wheeled::Car car, void* vehicle) {
    if (vehicle == g_vehicle) {
        return;
    }
    g_vehicle = vehicle;
    g_player = {car, vehicle != nullptr ? OfType(Vehicle::AnimType(vehicle)) : nullptr};
}

const Spec* OfPlayer(Wheeled::Car car) {
    const Followed followed = g_player;
    return followed.car == car ? followed.spec : nullptr;
}

std::span<const Spec> All() { return kVehicles; }

}
