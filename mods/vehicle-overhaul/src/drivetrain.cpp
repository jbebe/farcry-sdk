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
#include "gearbox.h"
#include "tuning.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    using VehicleOverhaul::Wheeled::Car;

    // Havok's engine, the same in every car: the bottom of its range and the revs its gearbox shifts up at,
    // laid over the real engine's idle and redline, so a run in any gear climbs to the redline before it
    // shifts. In top gear the redline is the limit.
    constexpr float kHavokMin = 1000.0f;
    constexpr float kHavokUpshift = 6500.0f;
    // For a car whose real engine is not known: Havok's own revs.
    constexpr float kIdle = 850.0f;
    constexpr float kRedline = kHavokUpshift;
    // As shares of the way from idle to the redline, so each engine keeps the Datsun's character over its own
    // range: how far a slipping clutch lets the engine rev while the car pulls away (2,500 rpm on the Datsun),
    // and per second how fast the engine gains revs and loses them with the throttle off (7,000 and 3,000).
    constexpr float kClutchSlip = 1750.0f / 5750.0f;
    constexpr float kRiseRate = 7000.0f / 5750.0f;
    constexpr float kFallRate = 3000.0f / 5750.0f;
    // A driver's foot is no machine: where the slipping clutch holds the revs wanders by up to this share of the
    // range, slowly, as two waves of these periods in seconds.
    constexpr float kSlipWander = 0.03f;
    constexpr double kWanderPeriods[2] = {2.2, 1.25};
    // The seconds the engine sound takes to fade in once the engine catches.
    constexpr float kCatchFade = 0.5f;
    // The seconds an automatic's torque converter takes to bring the revs to a new gear's.
    constexpr float kConverterGlide = 0.3f;

    // Kept by the physics step, read by the sound. The shift is the last gear change not yet sounded:
    // +1 up, -1 down.
    std::atomic<float> g_rpm{kIdle};
    std::atomic<int> g_shift{0};
    std::atomic<uint32_t> g_shiftSound{0};
    // The driver is off the throttle for a gear change.
    std::atomic<bool> g_lifted{false};
    // How far the engine sound has faded in since the engine caught: 0 before, 1 once in.
    std::atomic<float> g_caught{1.0f};
    // Whether the car's tuning has the overhaul and its real engine on, so the sound hears these revs.
    std::atomic<bool> g_heard{false};

    // The car the player drives, or null once they are out, so getting back in starts it afresh.
    std::atomic<Car> g_car{nullptr};
    float g_sinceEntry = 0.0f;
    // Seconds driven, in any car, which the clutch's wander follows.
    double g_clock = 0.0;
    int g_gear = 0;
    // The seconds the last gear change keeps the clutch out, and those gone since. It is sounded halfway,
    // where the lever goes across; until then it waits here.
    float g_shiftTime = 0.0f;
    float g_sinceShift = 0.0f;
    int g_pendingShift = 0;
    // An automatic's last gear change: the revs it glides from, and the seconds since.
    float g_glideFrom = 0.0f;
    float g_sinceGlide = kConverterGlide;

    // The entity of the vehicle the player drives, on the game thread.
    void* g_vehicle = nullptr;

    float IdleOf(const VehicleOverhaul::RealVehicle::Spec* real) {
        return real != nullptr && real->idle > 0.0f ? real->idle : kIdle;
    }

    float RedlineOf(const VehicleOverhaul::RealVehicle::Spec* real) {
        return real != nullptr && real->redline > 0.0f ? real->redline : kRedline;
    }

    void Tick(void* pawn, float*) {
        const VehicleOverhaul::Wheeled::Car car = VehicleOverhaul::Wheeled::Player();
        void* const vehicle = car != nullptr ? VehicleOverhaul::Vehicle::Current(pawn) : nullptr;
        VehicleOverhaul::RealVehicle::Follow(car, vehicle);
        g_vehicle = VehicleOverhaul::Entity::Of(vehicle);
        if (g_vehicle == nullptr) {
            g_car = nullptr;
        }
    }

    bool Heard(void* vehicle, VehicleOverhaul::VehicleSound::Engine& engine) {
        if (vehicle == nullptr || vehicle != g_vehicle || !g_heard) {
            return false;
        }
        engine.shift = g_shift.exchange(0);
        engine.rpm = g_rpm;
        engine.shiftSound = g_shiftSound;
        engine.lifted = g_lifted;
        engine.caught = g_caught;
        return true;
    }
}

namespace VehicleOverhaul::Drivetrain {

bool Install() {
    return Vehicle::Install() && VehicleSound::Install(&Heard) && PawnTick::Subscribe(&Tick);
}

float Step(Car car, const RealVehicle::Spec* real, const Wheeled::Readout& readout, float seconds) {
    const float idle = IdleOf(real);
    const float redline = RedlineOf(real);
    const Tuning::Values tuning = Tuning::Current(real);
    g_heard = tuning.enabled && tuning.realEngine;
    g_shiftSound = real != nullptr ? real->shiftSound : 0;
    if (car != g_car) {
        g_car = car;
        g_gear = readout.gear;
        g_shiftTime = 0.0f;
        g_pendingShift = 0;
        g_sinceGlide = kConverterGlide;
        g_rpm = idle;
        g_sinceEntry = 0.0f;
    }
    g_sinceEntry += seconds;
    const float catchTime = real != nullptr ? real->catchTime : 0.0f;
    g_caught = catchTime > 0.0f ? std::clamp((g_sinceEntry - catchTime) / kCatchFade, 0.0f, 1.0f) : 1.0f;
    g_sinceShift += seconds;
    g_sinceGlide += seconds;
    // No time to change gear in either way: an automatic, which changes under power with no lever to clunk.
    const bool automatic = tuning.shiftTime == 0.0f && tuning.downshiftTime == 0.0f;
    // First put in at a stand has no clutch out, and is not heard.
    const bool selected = readout.clutchLeft == 0.0f && readout.speed < Gearbox::kStanding;
    if (readout.gear != g_gear && !selected) {
        if (automatic) {
            g_glideFrom = g_rpm;
            g_sinceGlide = 0.0f;
        } else {
            g_pendingShift = readout.gear > g_gear ? 1 : -1;
            // The change came a step ago, which Havok has counted off its clutch time.
            g_shiftTime = readout.clutchLeft + seconds;
            g_sinceShift = 0.0f;
        }
    }
    g_gear = readout.gear;
    if (g_pendingShift != 0 && g_sinceShift >= 0.5f * g_shiftTime) {
        g_shift = g_pendingShift;
        g_pendingShift = 0;
    }
    g_lifted = !automatic && g_sinceShift < g_shiftTime;

    g_clock += seconds;
    const double turn = 2.0 * 3.14159265358979 * g_clock;
    const float wander =
        static_cast<float>(0.6 * std::sin(turn / kWanderPeriods[0]) + 0.4 * std::sin(turn / kWanderPeriods[1] + 1.3));

    const float range = redline - idle;
    const float wheels = idle + (std::abs(readout.rpm) - kHavokMin) * range / (kHavokUpshift - kHavokMin);
    float target = std::max(idle, wheels);
    const float slip = idle + (kClutchSlip + kSlipWander * wander) * range;
    if (wheels < slip) {
        target = std::max(target, idle + readout.throttle * (slip - idle));
    }
    target = std::min(target, redline);

    float rpm = g_rpm + std::clamp(target - g_rpm, -kFallRate * range * seconds, kRiseRate * range * seconds);
    if (g_sinceGlide < kConverterGlide) {
        // Eased in and out, onto the new gear's revs as they move.
        const float t = g_sinceGlide / kConverterGlide;
        rpm = g_glideFrom + (target - g_glideFrom) * t * t * (3.0f - 2.0f * t);
    }
    g_rpm = rpm;
    return rpm;
}

bool AtRedline(const Wheeled::Readout& readout) { return std::abs(readout.rpm) >= kHavokUpshift; }

float HavokRpm(const RealVehicle::Spec* real, float rpm) {
    const float idle = IdleOf(real);
    return kHavokMin + (rpm - idle) * (kHavokUpshift - kHavokMin) / (RedlineOf(real) - idle);
}

}
