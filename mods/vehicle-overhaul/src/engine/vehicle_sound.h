// A vehicle's engine sound and rev counter (CVehicleTypeWheeled), with the gear changes and the RPM
// they follow handed to the plugin for the vehicles it drives.
#pragma once

#include <cstddef>
#include <cstdint>

namespace VehicleOverhaul::VehicleSound {

struct Engine {
    // The gear change to sound this frame: +1 up, -1 down, 0 none.
    int shift;
    float rpm;
    // The sound a gear change plays, or 0 for the vehicle's own.
    uint32_t shiftSound;
    // Off the throttle for a gear change: the sound hears the pedal up.
    bool lifted;
};

// For the vehicle entity a sound belongs to: false leaves it to the game; true sets its engine.
using EngineFn = bool (*)(void* vehicle, Engine& engine);

// False, and logged, when this build lacks the update's gear and RPM steps.
bool Install(EngineFn engine);

// Called every frame with each vehicle entity and its sound, before the gear choice. Install first.
using ObserveFn = void (*)(void* vehicle, uint8_t* sound);
void Observe(ObserveFn observe);

// The sound of the last gear change handed over by EngineFn, or 0.
uint32_t ShiftSound();

// A sound a vehicle plays, at offsets on its sound.
struct Slot {
    const char* name;
    ptrdiff_t id;
    // The handle of the one playing, or 0 where the vehicle keeps none.
    ptrdiff_t handle;
    // The flag that is set while the game means it to loop, or 0 for a sound that is not a loop the
    // game starts once.
    ptrdiff_t looping;
    // The sound that stops it where no handle is kept, or 0.
    ptrdiff_t stop;
};

// Every sound the vehicle's own update starts, in the order its archetype lists them.
constexpr Slot kSlots[] = {
    {"Idle", 0x2A0, 0, 0x29C, 0x2A4},
    {"Engine", 0x2A8, 0x270, 0x29C, 0},
    {"Extra torque", 0x2AC, 0x278, 0x29C, 0},
    {"Ignition", 0x2B0, 0, 0, 0},
    {"Engine off", 0x2B4, 0, 0, 0},
    {"Frame (inside only)", 0x2B8, 0x274, 0x438, 0},
    {"Gear change", 0x2BC, 0, 0, 0},
    {"Gear change, worn", 0x2C0, 0, 0, 0},
    {"Gear change, broken", 0x2C4, 0, 0, 0},
    {"Throttle", 0x2C8, 0x288, 0, 0},
    {"Suspension, heavy", 0x2CC, 0, 0, 0},
    {"Suspension, medium", 0x2D0, 0, 0, 0},
    {"Suspension, light", 0x2D4, 0, 0, 0},
    {"Brake", 0x2D8, 0x284, 0, 0},
    {"Loop while moving", 0x2EC, 0x28C, 0x410, 0},
};

// The sound type the vehicle plays its sounds as, and the callbacks they play for.
int32_t Type(const uint8_t* sound);
void* Callbacks(uint8_t* sound);

}
