// The Havok vehicle under every wheeled car: which one the player drives, the physics step each is
// driven by, and the parts the overhaul tunes.
//
// See docs/docs/engine-internals/vehicle-physics.md.
#include "engine/wheeled.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    using VehicleOverhaul::Wheeled::Car;
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
    constexpr ptrdiff_t kVehicleTransmission = 0x2C;
    constexpr ptrdiff_t kVehicleBrake = 0x30;
    constexpr ptrdiff_t kVehicleSuspension = 0x34;
    constexpr ptrdiff_t kVehicleAerodynamics = 0x38;
    constexpr ptrdiff_t kVehicleDamper = 0x44;
    constexpr ptrdiff_t kVehicleWheelsInfo = 0x48;
    constexpr ptrdiff_t kVehicleRpm = 0xB8;
    constexpr ptrdiff_t kVehicleGear = 0xCD;

    constexpr ptrdiff_t kWheelInfoStride = 0xC0;
    constexpr ptrdiff_t kWheelInfoContactFriction = 0x20;
    constexpr ptrdiff_t kWheelInfoContactBody = 0x24;
    constexpr ptrdiff_t kWheelInfoSuspensionLength = 0x50;

    constexpr ptrdiff_t kDataTorqueFactors = 0x60;
    constexpr ptrdiff_t kDataWheels = 0x8C;
    constexpr ptrdiff_t kDataChassisResponse = 0x180;
    constexpr ptrdiff_t kDataInverseMass = 0x18C;
    constexpr ptrdiff_t kDataWheelStride = 0x28;
    constexpr ptrdiff_t kDataWheelRadius = 0x00;
    constexpr ptrdiff_t kDataWheelFriction = 0x0C;
    constexpr ptrdiff_t kDataWheelMaxFriction = 0x14;

    constexpr ptrdiff_t kTransmissionPrimaryRatio = 0x10;

    constexpr ptrdiff_t kBrakeWheels = 0x08;
    constexpr ptrdiff_t kBrakeWheelStride = 0x0C;
    constexpr ptrdiff_t kBrakeLockTime = 0x14;

    constexpr ptrdiff_t kSuspensionWheels = 0x08;
    constexpr ptrdiff_t kSuspensionWheelStride = 0x30;
    constexpr ptrdiff_t kSuspensionWheelDirection = 0x10;
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

    // CPhysWheeledVehicleEntityImpl::SetDriver, which is not a function start the address library
    // knows.
    FCSE::Relocation<SetDriverFn> g_setDriver{FCSE::Pattern(
        "56 57 8B 7C 24 0C 57 8B F1 E8 ?? ?? ?? ?? 83 FF 02 75 1F 8B 86 84 01 00 00 8B 48 24 C6 41 "
        "3D 01")};
    FCSE::Relocation<ActionFn> g_action{FCSE::Uplay(0x004AC3A0)};
    FCSE::Relocation<ActionFn> g_rollingResistance{FCSE::Uplay(0x004ABF70)};
    // hkpVehicleData::init, which derives the tyre solver's chassis terms from the centre of mass.
    FCSE::Relocation<InitDataFn> g_initData{FCSE::Uplay(0x00BF9C70)};

    SetDriverFn g_originalSetDriver = nullptr;
    ActionFn g_originalAction = nullptr;
    ActionFn g_originalRollingResistance = nullptr;

    VehicleOverhaul::Wheeled::StepFn g_step = nullptr;
    VehicleOverhaul::Wheeled::RollingFn g_rolling = nullptr;

    // Set on the game thread, read on the physics thread.
    std::atomic<Car> g_player{nullptr};

    template <typename T>
    T& At(const uint8_t* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(const_cast<uint8_t*>(object) + offset);
    }

    uint8_t* Vehicle(Car car) { return At<uint8_t*>(car, kCarVehicle); }

    // An hkArray's elements.
    uint8_t* Elements(const uint8_t* object, ptrdiff_t array) { return At<uint8_t*>(object, array); }

    int WheelCount(Car car) { return (std::min)(At<int>(car, kCarWheels), kMaxWheels); }

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
        g_step(car);
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
}

namespace VehicleOverhaul::Wheeled {

bool Install(StepFn step, RollingFn rolling) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_setDriver || !g_action || !g_rollingResistance || !g_initData) {
        api->Log("wheeled: the vehicle physics entry points were not found in this build");
        return false;
    }
    g_step = step;
    g_rolling = rolling;
    return api->Hook(reinterpret_cast<void*>(g_setDriver.address()), reinterpret_cast<void*>(&SetDriverDetour),
                     reinterpret_cast<void**>(&g_originalSetDriver)) &&
           api->Hook(reinterpret_cast<void*>(g_action.address()), reinterpret_cast<void*>(&ActionDetour),
                     reinterpret_cast<void**>(&g_originalAction)) &&
           api->Hook(reinterpret_cast<void*>(g_rollingResistance.address()),
                     reinterpret_cast<void*>(&RollingResistanceDetour),
                     reinterpret_cast<void**>(&g_originalRollingResistance));
}

Car Player() { return g_player; }

Parts PartsOf(Car car) {
    uint8_t* vehicle = Vehicle(car);
    uint8_t* data = At<uint8_t*>(vehicle, kVehicleData);
    uint8_t* brake = At<uint8_t*>(vehicle, kVehicleBrake);
    uint8_t* aerodynamics = At<uint8_t*>(vehicle, kVehicleAerodynamics);

    Parts parts{};
    parts.enginePower = &At<float>(car, kCarEnginePower);
    parts.climbPower = &At<float>(car, kCarClimbPower);
    parts.primaryRatio = &At<float>(At<uint8_t*>(vehicle, kVehicleTransmission), kTransmissionPrimaryRatio);
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
    parts.wheels = WheelCount(car);
    parts.vehicle = vehicle;

    uint8_t* wheels = Elements(data, kDataWheels);
    uint8_t* brakes = Elements(brake, kBrakeWheels);
    uint8_t* springs = Elements(At<uint8_t*>(vehicle, kVehicleSuspension), kSuspensionSprings);
    for (int i = 0; i < parts.wheels; ++i) {
        uint8_t* wheel = wheels + i * kDataWheelStride;
        float* spring = reinterpret_cast<float*>(springs + i * kSuspensionSpringStride);
        parts.wheel[i] = {&At<float>(wheel, kDataWheelFriction), &At<float>(wheel, kDataWheelMaxFriction),
                          reinterpret_cast<float*>(brakes + i * kBrakeWheelStride), &spring[0], &spring[1],
                          &spring[2]};
    }
    return parts;
}

Readout ReadoutOf(Car car) {
    uint8_t* vehicle = Vehicle(car);
    const float* velocity = &At<float>(At<uint8_t*>(vehicle, kVehicleChassis), kChassisVelocity);
    return {std::hypot(velocity[0], velocity[1], velocity[2]), At<float>(vehicle, kVehicleRpm),
            At<int8_t>(vehicle, kVehicleGear)};
}

Chassis ChassisOf(Car car) {
    uint8_t* vehicle = Vehicle(car);
    uint8_t* data = At<uint8_t*>(vehicle, kVehicleData);
    const uint8_t* body = At<uint8_t*>(vehicle, kVehicleChassis);
    const uint8_t* suspension = Elements(At<uint8_t*>(vehicle, kVehicleSuspension), kSuspensionWheels);
    const uint8_t* wheelData = Elements(data, kDataWheels);
    const uint8_t* wheelInfo = Elements(vehicle, kVehicleWheelsInfo);

    Chassis chassis{1.0f / At<float>(data, kDataInverseMass), At<float>(car, kCarRetailEnginePower)};
    std::copy_n(&At<float>(body, kChassisLocalCentreOfMass), 3, chassis.centreOfMass);

    const int wheels = WheelCount(car);
    if (wheels != kMaxWheels) {
        return chassis;
    }
    float x[kMaxWheels];
    int touching = 0;
    for (int i = 0; i < wheels; ++i) {
        const float* hardpoint = &At<float>(suspension, i * kSuspensionWheelStride);
        const float* direction = &At<float>(suspension, i * kSuspensionWheelStride + kSuspensionWheelDirection);
        const uint8_t* info = wheelInfo + i * kWheelInfoStride;
        const float centre = hardpoint[2] + direction[2] * At<float>(info, kWheelInfoSuspensionLength);
        const float radius = At<float>(wheelData, i * kDataWheelStride + kDataWheelRadius);
        x[i] = hardpoint[0];
        chassis.axles += centre / wheels;
        chassis.ground += (centre + direction[2] * radius) / wheels;
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

    g_initData(At<uint8_t*>(vehicle, kVehicleData), nullptr, At<uint8_t*>(vehicle, kVehicleSuspension) + kSuspensionWheels,
               body);
}

}
