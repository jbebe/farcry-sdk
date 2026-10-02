// The device methods a plugin hooks, named without ever touching the game's own device.
#pragma once

#include <cstddef>

namespace WeaponOverhaul::Vtable {

constexpr size_t kReset = 16;
constexpr size_t kEndScene = 42;
constexpr size_t kDrawIndexedPrimitive = 82;

// The function the game's device will call for `slot`, or null if no Direct3D device could be
// created to read it from. Every IDirect3DDevice9 in a process shares one vtable, so a throwaway
// device of our own names them all.
void* Slot(size_t slot);

}
