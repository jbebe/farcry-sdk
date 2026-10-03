// The engine of the car the player drives, as its sound and rev counter hear it: revs from the real
// gearbox, and its gear changes.
//
// The game makes up a car's revs from its road speed over three pretend gears. Here they follow the
// driven wheels through Havok's gearbox instead, with an engine's own weight on top: it idles, revs
// against a slipping clutch while the car pulls away, and runs down while a gear change has the clutch
// out.
#include "drivetrain.h"

#include "engine/entity.h"
#include "engine/pawn_tick.h"
#include "engine/vehicle.h"
#include "engine/vehicle_sound.h"
#include "tuning.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    using VehicleOverhaul::Wheeled::Car;

    // The span of Havok's engine, the same in every car, which is laid over the real engine's idle and
    // rev limit.
    constexpr float kHavokMin = 1000.0f;
    constexpr float kHavokMax = 7500.0f;
    // For a car whose real engine is not known.
    constexpr float kIdle = 850.0f;
    constexpr float kRedline = kHavokMax;
    // The revs a slipping clutch lets the engine reach while the car pulls away.
    constexpr float kClutchSlip = 2500.0f;
    // Per second: how fast the engine gains revs, and loses them with the throttle off.
    constexpr float kRiseRate = 7000.0f;
    constexpr float kFallRate = 3000.0f;

    // Kept by the physics step, read by the sound. The shift is the last gear change not yet sounded:
    // +1 up, -1 down.
    std::atomic<float> g_rpm{kIdle};
    std::atomic<int> g_shift{0};

    Car g_car = nullptr;
    int g_gear = 0;

    // The entity of the vehicle the player drives, on the game thread.
    void* g_vehicle = nullptr;

    void Tick(void* pawn, float*) {
        g_vehicle = VehicleOverhaul::Wheeled::Player() != nullptr
                        ? VehicleOverhaul::Entity::Of(VehicleOverhaul::Vehicle::Current(pawn))
                        : nullptr;
    }

    bool Engine(void* vehicle, int& shift, float& rpm) {
        if (vehicle == nullptr || vehicle != g_vehicle) {
            return false;
        }
        const VehicleOverhaul::Tuning::Values tuning = VehicleOverhaul::Tuning::Current();
        if (!tuning.enabled || !tuning.realEngine) {
            return false;
        }
        shift = g_shift.exchange(0);
        rpm = g_rpm;
        return true;
    }
}

namespace VehicleOverhaul::Drivetrain {

bool Install() {
    return Vehicle::Install() && VehicleSound::Install(&Engine) && PawnTick::Subscribe(&Tick);
}

float Step(Car car, const RealVehicle::Spec* real, const Wheeled::Readout& readout, float seconds) {
    const float idle = real != nullptr && real->idle > 0.0f ? real->idle : kIdle;
    const float redline = real != nullptr && real->redline > 0.0f ? real->redline : kRedline;
    if (car != g_car) {
        g_car = car;
        g_gear = readout.gear;
        g_rpm = idle;
    }
    if (readout.gear != g_gear) {
        g_shift = readout.gear > g_gear ? 1 : -1;
        g_gear = readout.gear;
    }

    const float wheels = idle + (std::abs(readout.rpm) - kHavokMin) * (redline - idle) / (kHavokMax - kHavokMin);
    float target = std::max(idle, wheels);
    if (wheels < kClutchSlip) {
        target = std::max(target, idle + readout.throttle * (kClutchSlip - idle));
    }
    target = std::min(target, redline);

    const float rpm = g_rpm + std::clamp(target - g_rpm, -kFallRate * seconds, kRiseRate * seconds);
    g_rpm = rpm;
    return rpm;
}

}
