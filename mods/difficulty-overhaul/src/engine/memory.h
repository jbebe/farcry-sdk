// Reading the engine's objects and hooking its functions.
#pragma once

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace DifficultyOverhaul {

// The value of type T at `offset` into an engine object.
template <class T>
T Read(const void* object, ptrdiff_t offset) {
    return *reinterpret_cast<const T*>(static_cast<const uint8_t*>(object) + offset);
}

template <class T>
void Write(void* object, ptrdiff_t offset, T value) {
    *reinterpret_cast<T*>(static_cast<uint8_t*>(object) + offset) = value;
}

// Hooks the engine function `site`, keeping what it called in `original`. False when the function
// was not found, which is not logged here, or when FCSE refused it, which FCSE logs.
template <class Fn>
bool Hook(const FCSE::Relocation<Fn>& site, Fn detour, Fn* original) {
    return site && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                            reinterpret_cast<void*>(detour),
                                            reinterpret_cast<void**>(original));
}

}
