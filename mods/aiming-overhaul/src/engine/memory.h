// Reading the engine's objects and hooking its functions.
#pragma once

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace AimingOverhaul {

// The field of type T at `offset` into an engine object.
template <class T>
T& Field(uint8_t* object, ptrdiff_t offset) {
    return *reinterpret_cast<T*>(object + offset);
}

// Hooks the engine function at `target`, keeping what it called in `original`. False when the
// function was not found, which is not logged here, or when FCSE refused it, which FCSE logs.
template <class Fn>
bool Hook(uintptr_t target, Fn detour, Fn* original) {
    return target != 0 && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(target),
                                                     reinterpret_cast<void*>(detour),
                                                     reinterpret_cast<void**>(original));
}

}
