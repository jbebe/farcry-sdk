// Havok's limited hinges on a body, and their motors.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::Hinge {

// The hkpConstraintInstance of the first limited hinge with a velocity motor on `body`, or null.
uint8_t* WithVelocityMotor(uint8_t* body);

// What hkpLimitedHingeConstraintData::setMotorActive does to the data: the motor drives the hinge, or
// its friction brakes it. The hinges of one model share their data, so this switches every one of them.
void SetMotor(uint8_t* hinge, bool on);

}
