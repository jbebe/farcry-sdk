// Direct3D 9 device methods, hooked without touching the game's own device.
#pragma once

#include <cstddef>

namespace InventoryOverhaul::Direct3D {

constexpr size_t kSetTexture = 65;

// Hooks the device method in `slot`, keeping what it called in `original`. False, and logged, when
// it cannot.
bool Hook(size_t slot, void* detour, void** original);

template <class Fn>
bool Hook(size_t slot, Fn detour, Fn* original) {
    return Hook(slot, reinterpret_cast<void*>(detour), reinterpret_cast<void**>(original));
}

}
