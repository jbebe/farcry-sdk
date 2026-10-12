// Havok's limited hinges on a body, and their motors.
#include "engine/hinge.h"

#include "engine/memory.h"

namespace {
    using VehicleOverhaul::At;

    // hkpEntity::m_constraintsSlave, the constraints whose second body this is.
    constexpr ptrdiff_t kEntitySlaves = 0xA0;
    constexpr ptrdiff_t kEntitySlaveCount = 0xA4;

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
}

namespace VehicleOverhaul::Hinge {

uint8_t* WithVelocityMotor(uint8_t* body) {
    uint8_t** slaves = At<uint8_t**>(body, kEntitySlaves);
    const int32_t count = At<int32_t>(body, kEntitySlaveCount);
    for (int32_t i = 0; i < count; ++i) {
        const uint8_t* data = At<uint8_t*>(slaves[i], kInstanceData);
        if (data == nullptr || At<uint16_t>(data, kDataFirstAtom) != kSetLocalTransformsAtom ||
            At<uint16_t>(data, kDataMotorAtom) != kAngMotorAtom) {
            continue;
        }
        const uint8_t* motor = At<uint8_t*>(data, kDataMotor);
        if (motor != nullptr && At<int8_t>(motor, kMotorType) == kVelocityMotor) {
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
