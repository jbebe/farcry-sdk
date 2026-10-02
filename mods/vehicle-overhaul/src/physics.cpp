// The off-road physics: the car the player drives, re-tuned every physics step while they drive it
// and put back as the game made it when they get out.
//
// Every value is written from the car's retail one each step, so a moved slider applies at once.
#include "physics.h"

#include "tuning.h"

#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {
    using VehicleOverhaul::Wheeled::Car;
    using VehicleOverhaul::Wheeled::Chassis;
    using VehicleOverhaul::Wheeled::kMaxWheels;
    using VehicleOverhaul::Wheeled::Parts;

    // A car of the game's, told apart by its retail mass and engine, and the static stability factor
    // of the real vehicle it depicts.
    struct RealVehicle {
        float mass;
        float enginePower;
        const char* name;
        float stability;
    };

    // Puts the weight at the wheels' centres.
    constexpr float kAtTheAxles = 0.0f;

    constexpr RealVehicle kRealVehicles[] = {
        {1600.0f, 95.0f, "Land Rover Series III", 0.90f},
        {1000.0f, 90.0f, "Datsun 1200", 1.30f},
        {800.0f, 87.0f, "Buggy", kAtTheAxles},
        {1500.0f, 95.0f, "Jeep Liberty", 1.07f},
        {1200.0f, 88.0f, "Jeep Wrangler", 1.20f},
        {1200.0f, 50.0f, "Jeep Wrangler taxi", 1.20f},
        {600.0f, 80.0f, "Quad", 1.00f},
        {1600.0f, 88.0f, "Unimog", 0.85f},
        {4000.0f, 150.0f, "ZIL-130", 0.85f},
    };

    const RealVehicle* Match(const Chassis& chassis) {
        for (const RealVehicle& real : kRealVehicles) {
            if (std::abs(real.mass - chassis.mass) < 1.0f && std::abs(real.enginePower - chassis.enginePower) < 0.5f) {
                return &real;
            }
        }
        return nullptr;
    }

    // The retail values of the car the overhaul is changing, or has yet to put back. Engine power,
    // gearing and the speed limiter are not kept: the game holds its own and resets them with the driver.
    struct Retail {
        Car car;
        const void* vehicle;
        float climbPower;
        float downforce;
        float spinDamping;
        float chassisResponse[3];
        float lockTime;
        struct {
            float friction;
            float maxFriction;
            float brakeTorque;
            float springStrength;
            float dampingCompression;
            float dampingRelaxation;
        } wheel[kMaxWheels];
        float centreOfMass[3];
        // Whether the centre of mass is where the real vehicle has it rather than where it was.
        bool centreMoved;
    };

    Retail g_retail{};

    // Kept by the physics step, shown by the window.
    VehicleOverhaul::Physics::Status g_status{};

    Retail Capture(Car car, const Parts& parts, const Chassis& chassis) {
        Retail retail{car, parts.vehicle, *parts.climbPower, *parts.downforce, *parts.spinDamping};
        std::copy_n(parts.chassisResponse, 3, retail.chassisResponse);
        retail.lockTime = *parts.lockTime;
        for (int i = 0; i < parts.wheels; ++i) {
            const VehicleOverhaul::Wheeled::Wheel& wheel = parts.wheel[i];
            retail.wheel[i] = {*wheel.friction,       *wheel.maxFriction,         *wheel.brakeTorque,
                               *wheel.springStrength, *wheel.dampingCompression, *wheel.dampingRelaxation};
        }
        std::copy_n(chassis.centreOfMass, 3, retail.centreOfMass);
        return retail;
    }

    void Write(const Parts& parts, const VehicleOverhaul::Tuning::Values& t) {
        *parts.enginePower = parts.retailEnginePower * t.enginePower;
        *parts.climbPower = g_retail.climbPower * t.climbAssist;
        *parts.primaryRatio = parts.retailPrimaryRatio / t.topSpeed;
        *parts.speedLimiter = 0;
        *parts.downforce = g_retail.downforce * t.downforce;
        *parts.spinDamping = g_retail.spinDamping * t.spinDamping;
        *parts.lockTime = t.lockTime;

        const float responses[3] = {t.pitchResponse, t.rollResponse, t.yawResponse};
        for (int axis = 0; axis < 3; ++axis) {
            const float factor = parts.torqueFactors[axis];
            parts.chassisResponse[axis] = g_retail.chassisResponse[axis] * (factor > 0.0f ? responses[axis] / factor : 1.0f);
        }

        for (int i = 0; i < parts.wheels; ++i) {
            const VehicleOverhaul::Wheeled::Wheel& wheel = parts.wheel[i];
            const auto& retail = g_retail.wheel[i];
            *wheel.friction = retail.friction * t.tyreGrip;
            *wheel.maxFriction = retail.maxFriction * t.tyreGrip;
            *wheel.brakeTorque = retail.brakeTorque * t.brakeTorque;
            *wheel.springStrength = retail.springStrength * t.springs;
            *wheel.dampingCompression = retail.dampingCompression * t.damping;
            *wheel.dampingRelaxation = retail.dampingRelaxation * t.damping;
        }
    }

    // `driving`: the player still drives it, with the overhaul switched off.
    void Restore(const Parts& parts, bool driving) {
        *parts.enginePower = parts.retailEnginePower;
        *parts.climbPower = g_retail.climbPower;
        *parts.primaryRatio = parts.retailPrimaryRatio;
        // As SetDriver leaves it for a human; any other change of driver has set it already.
        if (driving) {
            *parts.speedLimiter = 1;
        }
        *parts.downforce = g_retail.downforce;
        *parts.spinDamping = g_retail.spinDamping;
        *parts.lockTime = g_retail.lockTime;
        std::copy_n(g_retail.chassisResponse, 3, parts.chassisResponse);

        for (int i = 0; i < parts.wheels; ++i) {
            const VehicleOverhaul::Wheeled::Wheel& wheel = parts.wheel[i];
            const auto& retail = g_retail.wheel[i];
            *wheel.friction = retail.friction;
            *wheel.maxFriction = retail.maxFriction;
            *wheel.brakeTorque = retail.brakeTorque;
            *wheel.springStrength = retail.springStrength;
            *wheel.dampingCompression = retail.dampingCompression;
            *wheel.dampingRelaxation = retail.dampingRelaxation;
        }
    }

    // Puts the centre of mass where `real` has it, over the car's own track, or with `real` null back
    // where the game had it.
    void PlaceCentreOfMass(Car car, const Chassis& chassis, const RealVehicle* real) {
        float centre[3];
        std::copy_n(g_retail.centreOfMass, 3, centre);
        if (real != nullptr) {
            centre[2] = real->stability == kAtTheAxles ? chassis.axles
                                                       : chassis.ground + chassis.track / (2.0f * real->stability);
        }
        VehicleOverhaul::Wheeled::MoveCentreOfMass(car, centre);
        g_retail.centreMoved = real != nullptr;
    }

    void Step(Car car) {
        const Car player = VehicleOverhaul::Wheeled::Player();
        if (car != g_retail.car && car != player) {
            return;
        }

        const VehicleOverhaul::Tuning::Values tuning = VehicleOverhaul::Tuning::Current();
        const bool wanted = car == player && tuning.enabled;
        const Chassis chassis = VehicleOverhaul::Wheeled::ChassisOf(car);
        const RealVehicle* real = Match(chassis);
        if (car == player) {
            const float height = chassis.centreOfMass[2] - chassis.ground;
            g_status = {VehicleOverhaul::Wheeled::ReadoutOf(car), real != nullptr ? real->name : nullptr, height,
                        height > 0.0f ? chassis.track / (2.0f * height) : 0.0f, chassis.grip};
        }
        if (car != g_retail.car && !wanted) {
            return;
        }

        const Parts parts = VehicleOverhaul::Wheeled::PartsOf(car);
        // A new car at the address of one destroyed while tuned: none of its values are ours.
        if (car == g_retail.car && parts.vehicle != g_retail.vehicle) {
            g_retail = {};
        }
        if (car != g_retail.car) {
            if (!wanted) {
                return;
            }
            g_retail = Capture(car, parts, chassis);
            if (real == nullptr) {
                FCSE::Logf("physics: a %.0f kg car with %.0f N m is not one the overhaul knows; its centre of "
                           "mass stays the game's",
                           chassis.mass, chassis.enginePower);
            }
        }

        const RealVehicle* centre = wanted && tuning.realCentreOfMass ? real : nullptr;
        if ((centre != nullptr) != g_retail.centreMoved) {
            PlaceCentreOfMass(car, chassis, centre);
        }
        if (wanted) {
            Write(parts, tuning);
        } else {
            Restore(parts, car == player);
            g_retail = {};
        }
    }

    float Rolling(Car car) {
        return car == g_retail.car ? VehicleOverhaul::Tuning::Current().rollingResistance : 1.0f;
    }
}

namespace VehicleOverhaul::Physics {

bool Install() { return Wheeled::Install(&Step, &Rolling); }

bool Latest(Status& status) {
    if (Wheeled::Player() == nullptr) {
        return false;
    }
    status = g_status;
    return true;
}

}
