// A vehicle's breakable parts, through its CVehiclePhysComponent.
#pragma once

#include "engine/wheeled.h"

#include <cstdint>

namespace VehicleOverhaul::Parts {

// Called on the game thread each frame a vehicle moves, once its hinged parts' bones follow their bodies.
using MoveFn = void (*)(void* component);

// Calls `move` for every vehicle that moves. False, and logged, when this build lacks the hook.
bool Install(MoveFn move);

// Saves the part `name` in its first state, whatever state it is in. Once only; false, and logged,
// when this build lacks the hook.
bool SaveInFirstState(uint32_t name);

// The index of the part `name`, or -1 when the vehicle has none.
int Find(void* component, uint32_t name);

uint32_t State(void* component, int part);

// Moves a part on to a later state, as damage would, with no sound or effect. A state with a hinged
// body builds that body on its hinge.
void Advance(void* component, int part, uint32_t state);

// True once the engine has started and runs.
bool EngineRunning(void* component);

// The car the vehicle's frame part drives, or null.
Wheeled::Car Car(void* component);

}
