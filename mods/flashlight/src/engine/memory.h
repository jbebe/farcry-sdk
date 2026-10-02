// The engine's objects and code, read by offset.
#pragma once

#include <algorithm>
#include <cstddef>
#include <cstdint>

namespace Flashlight {

// Where the rel32 `call` instruction at `call` goes.
template <typename T>
T CallTarget(const uint8_t* call) {
    return reinterpret_cast<T>(call + 5 + *reinterpret_cast<const int32_t*>(call + 1));
}

template <typename T>
T& Field(uint8_t* object, ptrdiff_t offset) {
    return *reinterpret_cast<T*>(object + offset);
}

inline const float* Vec3(const uint8_t* object, ptrdiff_t offset) {
    return reinterpret_cast<const float*>(object + offset);
}

inline void SetVec3(uint8_t* object, ptrdiff_t offset, const float* value) {
    std::copy_n(value, 3, &Field<float>(object, offset));
}

}
