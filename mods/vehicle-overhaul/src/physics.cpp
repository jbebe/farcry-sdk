// The off-road physics: the car the player drives, re-tuned every physics step while they drive it
// and put back as the game made it when they get out. Each step also moves its engine on.
//
// Every value is written from the car's retail one each step, so a moved slider applies at once.
#include "physics.h"

#include "drivetrain.h"
#include "real_vehicle.h"
#include "tuning.h"

#include "fcse_api.h"

#include <algorithm>
#include <cstdint>

namespace {
    using VehicleOverhaul::Wheeled::Car;
    using VehicleOverhaul::Wheeled::Chassis;
    using VehicleOverhaul::Wheeled::kMaxWheels;
    using VehicleOverhaul::Wheeled::Parts;
    namespace RealVehicle = VehicleOverhaul::RealVehicle;

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
        float shiftTime;
        float steeringLock;
        float steeringAtSpeed;
        struct {
            float friction;
            float maxFriction;
            float brakeTorque;
            float springStrength;
            float dampingCompression;
            float dampingRelaxation;
            float suspensionLength;
        } wheel[kMaxWheels];
        float centreOfMass[3];
        // Whether the centre of mass is where the real vehicle has it rather than where it was.
        bool centreMoved;
        // The suspension travel the tyre solver was last built for.
        float solverTravel = 1.0f;
    };

    Retail g_retail{};

    // Kept by the physics step, shown by the window.
    VehicleOverhaul::Physics::Status g_status{};

    Retail Capture(Car car, const Parts& parts, const Chassis& chassis) {
        Retail retail{car, parts.vehicle, *parts.climbPower, *parts.downforce, *parts.spinDamping};
        std::copy_n(parts.chassisResponse, 3, retail.chassisResponse);
        retail.lockTime = *parts.lockTime;
        retail.shiftTime = *parts.shiftTime;
        retail.steeringLock = *parts.steeringLock;
        retail.steeringAtSpeed = *parts.steeringAtSpeed;
        for (int i = 0; i < parts.wheels; ++i) {
            const VehicleOverhaul::Wheeled::Wheel& wheel = parts.wheel[i];
            retail.wheel[i] = {*wheel.friction,       *wheel.maxFriction,         *wheel.brakeTorque,
                               *wheel.springStrength, *wheel.dampingCompression, *wheel.dampingRelaxation,
                               *wheel.suspensionLength};
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
        *parts.shiftTime = t.shiftTime;
        *parts.steeringLock = g_retail.steeringLock * t.steeringLock;
        *parts.steeringAtSpeed = g_retail.steeringAtSpeed * t.steeringAtSpeed;

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
            *wheel.dampingCompression = retail.dampingCompression * t.compressionDamping;
            *wheel.dampingRelaxation = retail.dampingRelaxation * t.reboundDamping;
            *wheel.suspensionLength = retail.suspensionLength * t.suspensionTravel;
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
        *parts.shiftTime = g_retail.shiftTime;
        *parts.steeringLock = g_retail.steeringLock;
        *parts.steeringAtSpeed = g_retail.steeringAtSpeed;
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
            *wheel.suspensionLength = retail.suspensionLength;
        }
    }

    // Puts the centre of mass where `real` has it, over the car's own track, or with `real` null back
    // where the game had it. The tyre solver is left to be rebuilt.
    void PlaceCentreOfMass(Car car, const Chassis& chassis, const RealVehicle::Spec* real) {
        float centre[3];
        std::copy_n(g_retail.centreOfMass, 3, centre);
        if (real != nullptr) {
            centre[2] = real->stability == RealVehicle::kAtTheAxles ? chassis.axles
                                                                    : chassis.ground + chassis.track / (2.0f * real->stability);
        }
        VehicleOverhaul::Wheeled::MoveCentreOfMass(car, centre);
        g_retail.centreMoved = real != nullptr;
    }

    void Step(Car car, float seconds) {
        const Car player = VehicleOverhaul::Wheeled::Player();
        if (car != g_retail.car && car != player) {
            return;
        }

        const VehicleOverhaul::Tuning::Values tuning = VehicleOverhaul::Tuning::Current();
        const bool wanted = car == player && tuning.enabled;
        const Chassis chassis = VehicleOverhaul::Wheeled::ChassisOf(car);
        const RealVehicle::Spec* real = RealVehicle::Match(chassis.mass, chassis.enginePower);
        if (car == player) {
            VehicleOverhaul::Wheeled::Readout readout = VehicleOverhaul::Wheeled::ReadoutOf(car);
            readout.rpm = VehicleOverhaul::Drivetrain::Step(car, real, readout, seconds);
            const float height = chassis.centreOfMass[2] - chassis.ground;
            g_status = {readout, real != nullptr ? real->name : nullptr, height,
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

        if (wanted) {
            Write(parts, tuning);
        } else {
            Restore(parts, car == player);
        }

        // After the values, since the solver is built from the suspensions' length.
        const RealVehicle::Spec* centre = wanted && tuning.realCentreOfMass ? real : nullptr;
        const float travel = wanted ? tuning.suspensionTravel : 1.0f;
        const bool moveCentre = (centre != nullptr) != g_retail.centreMoved;
        if (moveCentre) {
            PlaceCentreOfMass(car, chassis, centre);
        }
        if (moveCentre || travel != g_retail.solverTravel) {
            VehicleOverhaul::Wheeled::RebuildSolver(car);
            g_retail.solverTravel = travel;
        }
        if (!wanted) {
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
