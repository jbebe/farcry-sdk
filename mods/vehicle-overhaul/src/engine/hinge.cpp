// Havok's limited hinges on a body, and their motors.
#include "engine/hinge.h"

#include "engine/memory.h"

namespace {
    using VehicleOverhaul::At;

    // hkpEntity: the constraints Havok files on it as master, inline, and as slave, by pointer.
    constexpr ptrdiff_t kEntityMasters = 0x98;
    constexpr ptrdiff_t kEntityMasterCount = 0x9C;
    constexpr ptrdiff_t kEntitySlaves = 0xA0;
    constexpr ptrdiff_t kEntitySlaveCount = 0xA4;
    // hkConstraintInternal, a master's record of a constraint, which it begins with.
    constexpr ptrdiff_t kInternalStride = 0x30;

    constexpr ptrdiff_t kInstanceData = 0x0C;

    // hkpLimitedHingeConstraintData: its first atom's type, and its motor and friction atoms.
    constexpr ptrdiff_t kDataFirstAtom = 0x10;
    constexpr ptrdiff_t kDataMotorAtom = 0xA0;
    constexpr ptrdiff_t kDataMotorEnabled = 0xA2;
    constexpr ptrdiff_t kDataMotor = 0xB0;
    constexpr ptrdiff_t kDataFrictionEnabled = 0xB6;
    constexpr uint16_t kSetLocalTransformsAtom = 2;
    constexpr uint16_t kAngMotorAtom = 18;

    constexpr ptrdiff_t kMotorType = 0x08;
    constexpr int8_t kVelocityMotor = 2;

    bool HasVelocityMotor(const uint8_t* instance) {
        const uint8_t* data = At<uint8_t*>(instance, kInstanceData);
        if (data == nullptr || At<uint16_t>(data, kDataFirstAtom) != kSetLocalTransformsAtom ||
            At<uint16_t>(data, kDataMotorAtom) != kAngMotorAtom) {
            return false;
        }
        const uint8_t* motor = At<uint8_t*>(data, kDataMotor);
        return motor != nullptr && At<int8_t>(motor, kMotorType) == kVelocityMotor;
    }
}

namespace VehicleOverhaul::Hinge {

uint8_t* WithVelocityMotor(uint8_t* body) {
    const uint8_t* masters = At<uint8_t*>(body, kEntityMasters);
    for (int i = 0; i < At<uint16_t>(body, kEntityMasterCount); ++i) {
        uint8_t* instance = At<uint8_t*>(masters, i * kInternalStride);
        if (HasVelocityMotor(instance)) {
            return instance;
        }
    }
    uint8_t** slaves = At<uint8_t**>(body, kEntitySlaves);
    for (int32_t i = 0; i < At<int32_t>(body, kEntitySlaveCount); ++i) {
        if (HasVelocityMotor(slaves[i])) {
            return slaves[i];
        }
    }
    return nullptr;
}

void SetMotor(uint8_t* hinge, bool on) {
    uint8_t* data = At<uint8_t*>(hinge, kInstanceData);
    if ((At<uint8_t>(data, kDataMotorEnabled) != 0) != on) {
        At<uint8_t>(data, kDataMotorEnabled) = on;
        At<uint8_t>(data, kDataFrictionEnabled) = !on;
    }
}

}
