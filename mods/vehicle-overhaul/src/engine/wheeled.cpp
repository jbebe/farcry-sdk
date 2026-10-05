// The Havok vehicle under every wheeled car: which one the player drives, the physics step each is
// driven by, and the parts the overhaul tunes.
//
// See docs/docs/engine-internals/vehicle-physics.md.
#include "engine/wheeled.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    using VehicleOverhaul::At;
    using VehicleOverhaul::Wheeled::AxleOf;
    using VehicleOverhaul::Wheeled::Car;
    using VehicleOverhaul::Wheeled::kAxles;
    using VehicleOverhaul::Wheeled::kMaxWheels;

    // CPhysWheeledVehicleEntityImpl.
    constexpr ptrdiff_t kCarVehicle = 0x184;
    constexpr ptrdiff_t kCarWheels = 0x18C;
    constexpr ptrdiff_t kCarSuspension = 0x1C0;
    constexpr ptrdiff_t kCarEnginePower = 0x1C4;
    constexpr ptrdiff_t kCarClimbPower = 0x1C8;
    constexpr ptrdiff_t kCarRetailPrimaryRatio = 0x1DC;
    constexpr ptrdiff_t kCarRetailEnginePower = 0x1E0;

    // hkpVehicleInstance and the parts it points at.
    constexpr ptrdiff_t kVehicleChassis = 0x18;
    constexpr ptrdiff_t kVehicleData = 0x1C;
    constexpr ptrdiff_t kVehicleSteering = 0x24;
    constexpr ptrdiff_t kVehicleTransmission = 0x2C;
    constexpr ptrdiff_t kVehicleBrake = 0x30;
    constexpr ptrdiff_t kVehicleSuspension = 0x34;
    constexpr ptrdiff_t kVehicleAerodynamics = 0x38;
    constexpr ptrdiff_t kVehicleDamper = 0x44;
    constexpr ptrdiff_t kVehicleWheelsInfo = 0x48;
    constexpr ptrdiff_t kVehicleDriverInput = 0x9C;
    constexpr ptrdiff_t kVehicleRpm = 0xB8;
    constexpr ptrdiff_t kVehicleGear = 0xCD;

    // The driver input's forward axis: negative accelerates, which the game sets from its pedal.
    constexpr ptrdiff_t kDriverInputForward = 0x0C;
    // hkStepInfo's length of the step.
    constexpr ptrdiff_t kStepSeconds = 0x08;

    constexpr ptrdiff_t kWheelInfoStride = 0xC0;
    constexpr ptrdiff_t kWheelInfoContactFriction = 0x20;
    constexpr ptrdiff_t kWheelInfoContactBody = 0x24;
    constexpr ptrdiff_t kWheelInfoSuspensionLength = 0x50;

    // What the wheel collide found under each wheel this step.
    constexpr ptrdiff_t kCollidedStride = 0x40;
    constexpr ptrdiff_t kCollidedContactBody = 0x24;
    constexpr ptrdiff_t kCollidedLength = 0x2C;
    constexpr ptrdiff_t kCollidedClosingSpeed = 0x30;
    constexpr ptrdiff_t kCollidedSlant = 0x34;

    constexpr ptrdiff_t kDataTorqueFactors = 0x60;
    constexpr ptrdiff_t kDataWheels = 0x8C;
    constexpr ptrdiff_t kDataChassisResponse = 0x180;
    constexpr ptrdiff_t kDataInverseMass = 0x18C;
    constexpr ptrdiff_t kDataWheelStride = 0x28;
    constexpr ptrdiff_t kDataWheelRadius = 0x00;
    constexpr ptrdiff_t kDataWheelFriction = 0x0C;
    constexpr ptrdiff_t kDataWheelMaxFriction = 0x14;

    // CHkPhysVehicleSteering: the angles a held steering key winds the wheels out to standing still
    // (fLowMaxAngle) and at and above its speed (fHighMaxAngle).
    constexpr ptrdiff_t kSteeringLowMaxAngle = 0x28;
    constexpr ptrdiff_t kSteeringHighMaxAngle = 0x2C;

    constexpr ptrdiff_t kTransmissionPrimaryRatio = 0x10;
    constexpr ptrdiff_t kTransmissionClutchDelay = 0x14;

    constexpr ptrdiff_t kBrakeWheels = 0x08;
    constexpr ptrdiff_t kBrakeWheelStride = 0x0C;
    constexpr ptrdiff_t kBrakeLockTime = 0x14;

    constexpr ptrdiff_t kSuspensionWheels = 0x08;
    constexpr ptrdiff_t kSuspensionWheelStride = 0x30;
    constexpr ptrdiff_t kSuspensionWheelDirection = 0x10;
    constexpr ptrdiff_t kSuspensionWheelLength = 0x20;
    constexpr ptrdiff_t kSuspensionSprings = 0x14;
    constexpr ptrdiff_t kSuspensionSpringStride = 0x0C;
    // FC2's own: each wheel's suspension force last step, which rolling resistance scales.
    constexpr ptrdiff_t kSuspensionForces = 0x20;

    constexpr ptrdiff_t kAerodynamicsDownforce = 0x28;
    constexpr ptrdiff_t kAerodynamicsSpeedLimiter = 0x30;
    constexpr ptrdiff_t kDamperSpinDamping = 0x08;

    // The chassis hkpRigidBody's motion state: its rotation's columns, the swept centre of mass at
    // both ends of the step, and the centre of mass in the body's own space.
    constexpr ptrdiff_t kChassisRotation = 0xE0;
    constexpr ptrdiff_t kChassisCentreOfMass0 = 0x120;
    constexpr ptrdiff_t kChassisCentreOfMass1 = 0x130;
    constexpr ptrdiff_t kChassisLocalCentreOfMass = 0x160;
    constexpr ptrdiff_t kChassisVelocity = 0x1A0;

    // The wheels in the order the game sorts them: front left, front right, rear right, rear left.
    enum { kFrontLeft, kFrontRight, kRearRight, kRearLeft };

    constexpr int kHumanDriver = 1;

    using SetDriverFn = void(__fastcall*)(Car car, void* unused, int32_t driver);
    using ActionFn = void(__fastcall*)(Car car, void* unused, void* stepInfo);
    using InitDataFn = void(__fastcall*)(uint8_t* data, void* unused, void* suspensionWheels, uint8_t* chassis);
    using CalcSuspensionFn = void(__fastcall*)(uint8_t* suspension, void* unused, float seconds, uint8_t* vehicle,
                                               const uint8_t* collided, float* forces);
    using WheelRotationFn = float*(__fastcall*)(Car car, void* unused, float* rotation, uint32_t wheel, float steering);

    // CPhysWheeledVehicleEntityImpl::SetDriver, which is not a function start the address library
    // knows.
    FCSE::Relocation<SetDriverFn> g_setDriver{FCSE::Pattern(
        "56 57 8B 7C 24 0C 57 8B F1 E8 ?? ?? ?? ?? 83 FF 02 75 1F 8B 86 84 01 00 00 8B 48 24 C6 41 "
        "3D 01")};
    FCSE::Relocation<ActionFn> g_action{FCSE::Uplay(0x004AC3A0)};
    FCSE::Relocation<ActionFn> g_rollingResistance{FCSE::Uplay(0x004ABF70)};
    // hkpVehicleData::init, which derives the tyre solver's chassis terms from the centre of mass.
    FCSE::Relocation<InitDataFn> g_initData{FCSE::Uplay(0x00BF9C70)};
    // hkpVehicleDefaultSuspension::calcSuspension, Havok's springs.
    FCSE::Relocation<CalcSuspensionFn> g_calcSuspension{FCSE::Uplay(0x00BFA510)};
    // CPhysWheeledVehicleEntityImpl::GetWheelLocalOrientation, a wheel's rotation as it is drawn.
    FCSE::Relocation<WheelRotationFn> g_wheelRotation{FCSE::Uplay(0x004AB1E0)};

    SetDriverFn g_originalSetDriver = nullptr;
    ActionFn g_originalAction = nullptr;
    ActionFn g_originalRollingResistance = nullptr;
    CalcSuspensionFn g_originalCalcSuspension = nullptr;
    WheelRotationFn g_originalWheelRotation = nullptr;

    VehicleOverhaul::Wheeled::StepFn g_step = nullptr;
    VehicleOverhaul::Wheeled::RollingFn g_rolling = nullptr;
    VehicleOverhaul::Wheeled::SuspensionFn g_suspension = nullptr;

    // Set on the game thread, read on the physics thread.
    std::atomic<Car> g_player{nullptr};
    // Set on the physics thread, read where the wheels are drawn.
    std::atomic<float> g_lean[kAxles];

    uint8_t* Vehicle(Car car) { return At<uint8_t*>(car, kCarVehicle); }

    // An hkArray's elements.
    uint8_t* Elements(const uint8_t* object, ptrdiff_t array) { return At<uint8_t*>(object, array); }

    int WheelCount(Car car) { return (std::min)(At<int>(car, kCarWheels), kMaxWheels); }

    float Mass(const uint8_t* vehicle) { return 1.0f / At<float>(At<uint8_t*>(vehicle, kVehicleData), kDataInverseMass); }

    // Turns a wheel's rotation, as the game builds it, by `angle` about the chassis's forward axis after
    // its spin and steering. The game draws the inverse, so the turn goes on the right, reversed.
    void Lean(float* rotation, float angle) {
        const float s = -std::sin(angle / 2.0f);
        const float c = std::cos(angle / 2.0f);
        const float x = rotation[0];
        const float y = rotation[1];
        const float z = rotation[2];
        const float w = rotation[3];
        rotation[0] = x * c - z * s;
        rotation[1] = w * s + y * c;
        rotation[2] = x * s + z * c;
        rotation[3] = w * c - y * s;
    }

    void __fastcall SetDriverDetour(Car car, void* unused, int32_t driver) {
        g_originalSetDriver(car, unused, driver);
        if (driver == kHumanDriver) {
            g_player = car;
        } else {
            Car player = car;
            g_player.compare_exchange_strong(player, nullptr);
        }
    }

    void __fastcall ActionDetour(Car car, void* unused, void* stepInfo) {
        g_step(car, At<float>(static_cast<uint8_t*>(stepInfo), kStepSeconds));
        g_originalAction(car, unused, stepInfo);
    }

    void __fastcall RollingResistanceDetour(Car car, void* unused, void* stepInfo) {
        const float share = g_rolling(car);
        if (share >= 1.0f) {
            g_originalRollingResistance(car, unused, stepInfo);
            return;
        }

        float* forces = reinterpret_cast<float*>(Elements(At<uint8_t*>(car, kCarSuspension), kSuspensionForces));
        const int wheels = WheelCount(car);
        float kept[kMaxWheels];
        for (int i = 0; i < wheels; ++i) {
            kept[i] = forces[i];
            forces[i] *= share;
        }
        g_originalRollingResistance(car, unused, stepInfo);
        std::copy_n(kept, wheels, forces);
    }

    void __fastcall CalcSuspensionDetour(uint8_t* suspension, void* unused, float seconds, uint8_t* vehicle,
                                         const uint8_t* collided, float* forces) {
        g_originalCalcSuspension(suspension, unused, seconds, vehicle, collided, forces);
        const Car player = g_player;
        if (player == nullptr || Vehicle(player) != vehicle) {
            return;
        }
        const uint8_t* params = Elements(suspension, kSuspensionWheels);
        const uint8_t* springs = Elements(suspension, kSuspensionSprings);
        VehicleOverhaul::Wheeled::Suspension wheels[kMaxWheels]{};
        for (int i = 0; i < WheelCount(player); ++i) {
            const uint8_t* wheel = collided + i * kCollidedStride;
            const float* spring = &At<float>(springs, i * kSuspensionSpringStride);
            wheels[i] = {At<void*>(wheel, kCollidedContactBody) != nullptr,
                         At<float>(wheel, kCollidedLength),
                         At<float>(wheel, kCollidedClosingSpeed),
                         At<float>(wheel, kCollidedSlant),
                         At<float>(params, i * kSuspensionWheelStride + kSuspensionWheelLength),
                         spring[0],
                         spring[1],
                         spring[2]};
        }
        g_suspension(player, Mass(vehicle), wheels, forces);
    }

    float* __fastcall WheelRotationDetour(Car car, void* unused, float* rotation, uint32_t wheel, float steering) {
        g_originalWheelRotation(car, unused, rotation, wheel, steering);
        if (car == g_player && wheel < kMaxWheels) {
            const float angle = g_lean[AxleOf(wheel)];
            if (angle != 0.0f) {
                Lean(rotation, angle);
            }
        }
        return rotation;
    }
}

namespace VehicleOverhaul::Wheeled {

bool Install(StepFn step, RollingFn rolling, SuspensionFn suspension) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_setDriver || !g_action || !g_rollingResistance || !g_initData || !g_calcSuspension || !g_wheelRotation) {
        api->Log("wheeled: the vehicle physics entry points were not found in this build");
        return false;
    }
    g_step = step;
    g_rolling = rolling;
    g_suspension = suspension;
    return api->Hook(reinterpret_cast<void*>(g_setDriver.address()), reinterpret_cast<void*>(&SetDriverDetour),
                     reinterpret_cast<void**>(&g_originalSetDriver)) &&
           api->Hook(reinterpret_cast<void*>(g_action.address()), reinterpret_cast<void*>(&ActionDetour),
                     reinterpret_cast<void**>(&g_originalAction)) &&
           api->Hook(reinterpret_cast<void*>(g_rollingResistance.address()),
                     reinterpret_cast<void*>(&RollingResistanceDetour),
                     reinterpret_cast<void**>(&g_originalRollingResistance)) &&
           api->Hook(reinterpret_cast<void*>(g_calcSuspension.address()),
                     reinterpret_cast<void*>(&CalcSuspensionDetour),
                     reinterpret_cast<void**>(&g_originalCalcSuspension)) &&
           api->Hook(reinterpret_cast<void*>(g_wheelRotation.address()), reinterpret_cast<void*>(&WheelRotationDetour),
                     reinterpret_cast<void**>(&g_originalWheelRotation));
}

Car Player() { return g_player; }

void WheelCentre(Car car, int wheel, float* centre) {
    const uint8_t* vehicle = Vehicle(car);
    const uint8_t* suspension =
        Elements(At<uint8_t*>(vehicle, kVehicleSuspension), kSuspensionWheels) + wheel * kSuspensionWheelStride;
    const float* hardpoint = &At<float>(suspension, 0);
    const float* direction = &At<float>(suspension, kSuspensionWheelDirection);
    const float length = (std::max)(
        0.0f, At<float>(Elements(vehicle, kVehicleWheelsInfo), wheel * kWheelInfoStride + kWheelInfoSuspensionLength));
    for (int axis = 0; axis < 3; ++axis) {
        centre[axis] = hardpoint[axis] + direction[axis] * length;
    }
}

void LeanWheels(const float* angles) {
    for (int axle = 0; axle < kAxles; ++axle) {
        g_lean[axle] = angles[axle];
    }
}

Parts PartsOf(Car car) {
    uint8_t* vehicle = Vehicle(car);
    uint8_t* data = At<uint8_t*>(vehicle, kVehicleData);
    uint8_t* brake = At<uint8_t*>(vehicle, kVehicleBrake);
    uint8_t* aerodynamics = At<uint8_t*>(vehicle, kVehicleAerodynamics);

    Parts parts{};
    parts.enginePower = &At<float>(car, kCarEnginePower);
    parts.climbPower = &At<float>(car, kCarClimbPower);
    uint8_t* transmission = At<uint8_t*>(vehicle, kVehicleTransmission);
    parts.primaryRatio = &At<float>(transmission, kTransmissionPrimaryRatio);
    parts.shiftTime = &At<float>(transmission, kTransmissionClutchDelay);
    parts.retailEnginePower = At<float>(car, kCarRetailEnginePower);
    parts.retailPrimaryRatio = At<float>(car, kCarRetailPrimaryRatio);
    parts.speedLimiter = &At<uint8_t>(aerodynamics, kAerodynamicsSpeedLimiter);
    parts.downforce = &At<float>(aerodynamics, kAerodynamicsDownforce);
    parts.spinDamping = &At<float>(At<uint8_t*>(vehicle, kVehicleDamper), kDamperSpinDamping);
    parts.chassisResponse = &At<float>(data, kDataChassisResponse);
    // Stored roll, pitch, yaw; the response is pitch, roll, yaw.
    const float* factors = &At<float>(data, kDataTorqueFactors);
    parts.torqueFactors[0] = factors[1];
    parts.torqueFactors[1] = factors[0];
    parts.torqueFactors[2] = factors[2];
    parts.lockTime = &At<float>(brake, kBrakeLockTime);
    uint8_t* steering = At<uint8_t*>(vehicle, kVehicleSteering);
    parts.steeringLock = &At<float>(steering, kSteeringLowMaxAngle);
    parts.steeringAtSpeed = &At<float>(steering, kSteeringHighMaxAngle);
    parts.wheels = WheelCount(car);
    parts.vehicle = vehicle;

    uint8_t* wheels = Elements(data, kDataWheels);
    uint8_t* brakes = Elements(brake, kBrakeWheels);
    uint8_t* suspension = At<uint8_t*>(vehicle, kVehicleSuspension);
    uint8_t* springs = Elements(suspension, kSuspensionSprings);
    uint8_t* suspensionWheels = Elements(suspension, kSuspensionWheels);
    for (int i = 0; i < parts.wheels; ++i) {
        uint8_t* wheel = wheels + i * kDataWheelStride;
        float* spring = reinterpret_cast<float*>(springs + i * kSuspensionSpringStride);
        parts.wheel[i] = {&At<float>(wheel, kDataWheelFriction), &At<float>(wheel, kDataWheelMaxFriction),
                          reinterpret_cast<float*>(brakes + i * kBrakeWheelStride), &spring[0], &spring[1],
                          &spring[2], &At<float>(suspensionWheels, i * kSuspensionWheelStride + kSuspensionWheelLength)};
    }
    return parts;
}

Readout ReadoutOf(Car car) {
    uint8_t* vehicle = Vehicle(car);
    const float* velocity = &At<float>(At<uint8_t*>(vehicle, kVehicleChassis), kChassisVelocity);
    const float forward = At<float>(At<uint8_t*>(vehicle, kVehicleDriverInput), kDriverInputForward);
    return {std::hypot(velocity[0], velocity[1], velocity[2]), At<float>(vehicle, kVehicleRpm),
            At<int8_t>(vehicle, kVehicleGear), (std::max)(0.0f, -forward)};
}

Chassis ChassisOf(Car car) {
    uint8_t* vehicle = Vehicle(car);
    uint8_t* data = At<uint8_t*>(vehicle, kVehicleData);
    const uint8_t* body = At<uint8_t*>(vehicle, kVehicleChassis);
    const uint8_t* suspension = Elements(At<uint8_t*>(vehicle, kVehicleSuspension), kSuspensionWheels);
    const uint8_t* wheelData = Elements(data, kDataWheels);
    const uint8_t* wheelInfo = Elements(vehicle, kVehicleWheelsInfo);

    Chassis chassis{Mass(vehicle), At<float>(car, kCarRetailEnginePower)};
    std::copy_n(&At<float>(body, kChassisLocalCentreOfMass), 3, chassis.centreOfMass);

    const int wheels = WheelCount(car);
    if (wheels != kMaxWheels) {
        return chassis;
    }
    float x[kMaxWheels];
    int touching = 0;
    for (int i = 0; i < wheels; ++i) {
        float centre[3];
        WheelCentre(car, i, centre);
        const float* direction = &At<float>(suspension, i * kSuspensionWheelStride + kSuspensionWheelDirection);
        const uint8_t* info = wheelInfo + i * kWheelInfoStride;
        const float radius = At<float>(wheelData, i * kDataWheelStride + kDataWheelRadius);
        x[i] = centre[0];
        chassis.axles += centre[2] / wheels;
        chassis.ground += (centre[2] + direction[2] * radius) / wheels;
        if (At<void*>(info, kWheelInfoContactBody) != nullptr) {
            chassis.grip += At<float>(info, kWheelInfoContactFriction) *
                            At<float>(wheelData, i * kDataWheelStride + kDataWheelFriction);
            ++touching;
        }
    }
    chassis.grip = touching > 0 ? chassis.grip / touching : 0.0f;
    chassis.track = (std::abs(x[kFrontRight] - x[kFrontLeft]) + std::abs(x[kRearRight] - x[kRearLeft])) / 2.0f;
    return chassis;
}

void MoveCentreOfMass(Car car, const float* local) {
    uint8_t* vehicle = Vehicle(car);
    uint8_t* body = At<uint8_t*>(vehicle, kVehicleChassis);
    float* centre = &At<float>(body, kChassisLocalCentreOfMass);
    const float* rotation = &At<float>(body, kChassisRotation);

    // As hkSweptTransformUtil::setCentreOfRotationLocal: the body stays put, its centre moves.
    float offset[3];
    for (int axis = 0; axis < 3; ++axis) {
        offset[axis] = local[axis] - centre[axis];
        centre[axis] = local[axis];
    }
    float* swept0 = &At<float>(body, kChassisCentreOfMass0);
    float* swept1 = &At<float>(body, kChassisCentreOfMass1);
    for (int axis = 0; axis < 3; ++axis) {
        const float world = rotation[axis] * offset[0] + rotation[4 + axis] * offset[1] + rotation[8 + axis] * offset[2];
        swept0[axis] += world;
        swept1[axis] += world;
    }
}

void RebuildSolver(Car car) {
    uint8_t* vehicle = Vehicle(car);
    uint8_t* data = At<uint8_t*>(vehicle, kVehicleData);
    float* response = &At<float>(data, kDataChassisResponse);
    float kept[3];
    std::copy_n(response, 3, kept);
    g_initData(data, nullptr, At<uint8_t*>(vehicle, kVehicleSuspension) + kSuspensionWheels,
               At<uint8_t*>(vehicle, kVehicleChassis));
    std::copy_n(kept, 3, response);
}

}
