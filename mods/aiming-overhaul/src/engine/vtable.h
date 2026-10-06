// The device methods a plugin hooks, named without ever touching the game's own device.
#pragma once

#include <cstddef>

namespace AimingOverhaul::Vtable {

constexpr size_t kReset = 16;
constexpr size_t kEndScene = 42;
constexpr size_t kSetTexture = 65;
constexpr size_t kDrawPrimitive = 81;
constexpr size_t kDrawIndexedPrimitive = 82;
constexpr size_t kDrawPrimitiveUP = 83;
constexpr size_t kDrawIndexedPrimitiveUP = 84;

// Hooks `slot`, keeping what it called in `original`. False, and logged, when it cannot.
bool Hook(size_t slot, void* detour, void** original);

template <class Fn>
bool Hook(size_t slot, Fn detour, Fn* original) {
    return Hook(slot, reinterpret_cast<void*>(detour), reinterpret_cast<void**>(original));
}

}
