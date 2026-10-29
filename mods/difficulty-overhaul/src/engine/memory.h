// Reading the engine's objects and hooking its functions.
#pragma once

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>
#include <unordered_map>

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

// The three floats at `offset` into an engine object.
inline const float* Vec3(const void* object, ptrdiff_t offset) {
    return reinterpret_cast<const float*>(static_cast<const uint8_t*>(object) + offset);
}

// An engine object's entry in a feature's map; past 512 objects it forgets them all, since dead
// ones are never erased.
template <typename T>
T& Remember(std::unordered_map<const void*, T>& objects, const void* object) {
    if (objects.size() > 512) {
        objects.clear();
    }
    return objects[object];
}

// Hooks the engine function `site`, keeping what it called in `original`. False when the function
// was not found, which is not logged here, or when FCSE refused it, which FCSE logs.
template <class Fn>
bool Hook(const FCSE::Relocation<Fn>& site, Fn detour, Fn* original) {
    return site && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                            reinterpret_cast<void*>(detour),
                                            reinterpret_cast<void**>(original));
}

// Points the absolute memory operand `operand` bytes into the code at `site` at `value`, so the
// instruction reads the plugin's float instead of a constant the game shares. False as Hook is.
inline bool RedirectOperand(const FCSE::Relocation<uint8_t*>& site, ptrdiff_t operand,
                            float* value) {
    return site && FCSE::ApiPointer()->Patch(site.get() + operand, &value, sizeof(value));
}

}
