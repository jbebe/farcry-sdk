// Every value the driving is tuned by, kept in bin\vehicle-overhaul.ini and edited in DevTools'
// overlay.
#pragma once

namespace VehicleOverhaul::Tuning {

// Multiples of the car's own retail value, except the responses (Havok's factors, 1 = physical) and
// the lock time (seconds).
struct Values {
    // Whether the player's car is tuned at all, and whether a car the overhaul knows carries its
    // weight as high as the real vehicle it depicts.
    bool enabled;
    bool realCentreOfMass;
    float enginePower;
    float climbAssist;
    float topSpeed;
    float brakeTorque;
    float lockTime;
    float tyreGrip;
    float downforce;
    float rollingResistance;
    float pitchResponse;
    float rollResponse;
    float yawResponse;
    float spinDamping;
    float springs;
    float damping;
};

Values Current();

// Reads bin\vehicle-overhaul.ini over the defaults and writes the file back complete, which is also
// how it first appears.
void Load();

// Draws the window in DevTools' overlay.
void DrawWindow(void* userData);

}
