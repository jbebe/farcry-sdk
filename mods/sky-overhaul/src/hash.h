// The integer hash the noise, the aircraft trails and the weather are all made from.
#pragma once

#include <cstdint>

namespace SkyOverhaul {

inline uint32_t Hash(int x, int y, int z, uint32_t seed) {
    uint32_t value = static_cast<uint32_t>(x) * 0x8DA6B343u + static_cast<uint32_t>(y) * 0xD8163841u +
                     static_cast<uint32_t>(z) * 0xCB1AB31Fu + seed * 0x165667B1u;
    value ^= value >> 15;
    value *= 0x2C1B3C6Du;
    value ^= value >> 12;
    value *= 0x297A2D39u;
    value ^= value >> 15;
    return value;
}

// The hash's low 24 bits as a number from 0 to 1.
inline float Unit(uint32_t hash) {
    return static_cast<float>(hash & 0xFFFFFFu) / 16777215.0f;
}

// A number from 0 to 1 that depends only on its arguments.
inline float Random(int x, int y, int z, uint32_t seed) {
    return Unit(Hash(x, y, z, seed));
}

// The next number from 0 to 1 of the xorshift stream kept in `state`, which must not start at 0.
inline float NextUnit(uint32_t& state) {
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    return static_cast<float>(state >> 8) / 16777216.0f;
}

}
