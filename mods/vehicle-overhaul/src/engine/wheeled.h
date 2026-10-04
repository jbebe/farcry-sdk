// The Havok vehicle under every wheeled car: which one the player drives, the physics step each is
// driven by, and the parts the overhaul tunes.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::Wheeled {

// A car's CPhysWheeledVehicleEntityImpl, the game's owner of its Havok vehicle.
using Car = uint8_t*;

// Called on the physics thread for every car, each step of `seconds` before the game and Havok drive
// it.
using StepFn = void (*)(Car car, float seconds);

// Called on the physics thread before a car's rolling resistance; the share of it to apply.
using RollingFn = float (*)(Car car);

// False, and logged, when this build lacks anything the steps need.
bool Install(StepFn step, RollingFn rolling);

// The car the player is driving, or null. Any thread.
Car Player();

constexpr int kMaxWheels = 4;

struct Wheel {
    // The tyre's grip, multiplied by the ground's own, and the most it may reach.
    float* friction;
    float* maxFriction;
    float* brakeTorque;
    // Per kilogram of chassis.
    float* springStrength;
    float* dampingCompression;
    float* dampingRelaxation;
    // The suspension's length at rest, which is also its travel.
    float* suspensionLength;
};

// Every value the overhaul writes on one car.
struct Parts {
    // The peak torque the engine is rebuilt from every step, and what it gains with the nose up.
    float* enginePower;
    float* climbPower;
    float* primaryRatio;
    // The game's own copies of the car's engine power and gearing, which every change of driver
    // restores.
    float retailEnginePower;
    float retailPrimaryRatio;
    // Set while a human or no one drives; 1 holds the car near 60 km/h.
    uint8_t* speedLimiter;
    // The extra pull down on top of gravity, in m/s², negative.
    float* downforce;
    float* spinDamping;
    // How readily the tyres pitch, roll and yaw the chassis, as Havok derived it from the factors.
    float* chassisResponse;
    // The pitch, roll and yaw factors it was derived from.
    float torqueFactors[3];
    // Seconds a fully pressed brake takes to lock a wheel, and a gear change cuts the drive.
    float* lockTime;
    float* shiftTime;
    // How far a held steering key winds the front wheels out standing still, and at speed.
    float* steeringLock;
    float* steeringAtSpeed;
    int wheels;
    Wheel wheel[kMaxWheels];
    // Which Havok vehicle these belong to, to tell a car from a later one at the same address.
    const void* vehicle;
};

Parts PartsOf(Car car);

// What the car is doing.
struct Readout {
    float speed;
    // The driven wheels' spin through the gearbox, signed; the engine's revs once the clutch is in.
    float rpm;
    // From 0 in first; Havok shifts at once and then cuts the drive for the shift time.
    int gear;
    float throttle;
};

Readout ReadoutOf(Car car);

// The chassis as Havok holds it, in chassis space: x right, y forward, z up.
struct Chassis {
    float mass;
    // The engine's retail peak torque.
    float enginePower;
    float centreOfMass[3];
    // The height of the wheels' contact with the ground, and of their centres, at their present travel.
    float ground;
    float axles;
    float track;
    // The tyres' friction times the ground's, over the wheels touching it; 0 in the air.
    float grip;
};

Chassis ChassisOf(Car car);

// Moves the chassis's centre of mass, keeping the body where it is. Physics thread only.
void MoveCentreOfMass(Car car, const float* local);

// Rebuilds the tyre solver around the centre of mass and the suspensions' length, keeping the chassis
// response as it is. Physics thread only.
void RebuildSolver(Car car);

}
