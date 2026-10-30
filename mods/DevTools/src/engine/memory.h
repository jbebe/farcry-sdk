// Reading the engine's objects: a field by its offset, a virtual method by its slot, and a string.
#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace DevTools {

template <class T>
T Read(const void* object, ptrdiff_t offset) {
    T value;
    std::memcpy(&value, static_cast<const uint8_t*>(object) + offset, sizeof(T));
    return value;
}

// The virtual method at `slot` of `object`.
template <class Fn>
Fn Method(const void* object, size_t slot) {
    return reinterpret_cast<Fn>(Read<void* const*>(object, 0)[slot]);
}

// MSVC's std::string as this build lays it out.
struct NarrowString {
    const void* proxy;
    char buffer[16];
    uint32_t size;
    uint32_t capacity;

    const char* Text() const {
        return capacity < sizeof(buffer) ? buffer : *reinterpret_cast<const char* const*>(buffer);
    }
};
static_assert(sizeof(NarrowString) == 28, "the engine reads this as its own std::string");

}
