// The engine's objects, read by offset.
#pragma once

#include <cstddef>
#include <cstdint>

namespace VehicleOverhaul {

template <typename T>
T& At(const void* object, ptrdiff_t offset) {
    return *reinterpret_cast<T*>(static_cast<uint8_t*>(const_cast<void*>(object)) + offset);
}

}
