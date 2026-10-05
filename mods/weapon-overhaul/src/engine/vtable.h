// The device methods a plugin hooks, named without ever touching the game's own device.
#pragma once

#include <cstddef>

namespace WeaponOverhaul::Vtable {

constexpr size_t kReset = 16;
constexpr size_t kCreateTexture = 23;
constexpr size_t kEndScene = 42;
constexpr size_t kSetTexture = 65;
constexpr size_t kDrawPrimitive = 81;
constexpr size_t kDrawIndexedPrimitive = 82;
constexpr size_t kDrawPrimitiveUP = 83;
constexpr size_t kDrawIndexedPrimitiveUP = 84;

// The function the game's device will call for `slot`, or null if no Direct3D device could be
// created to read it from. Every IDirect3DDevice9 in a process shares one vtable, so a throwaway
// device of our own names them all.
void* Slot(size_t slot);

// Hooks `slot`, keeping what it called in `original`. False, and logged, when it cannot.
bool Hook(size_t slot, void* detour, void** original);

}
