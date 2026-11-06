// IDirect3DDevice9's functions, read off a device made for the purpose. The game's device runs the
// same code.
#pragma once

#include "engine/memory.h"

#include <cstddef>
#include <cstdint>

namespace PlayerBody::Vtable {

// The function at `slot` of IDirect3DDevice9's vtable, as declared in d3d9.h, or null, and logged,
// when no device could be made.
void* Slot(size_t slot);

template <class Fn>
bool Hook(size_t slot, Fn detour, Fn* original) {
    return PlayerBody::Hook(reinterpret_cast<uintptr_t>(Slot(slot)), detour, original);
}

}
