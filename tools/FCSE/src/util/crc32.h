#pragma once

#include <cstdint>
#include <string_view>

namespace FCSE {

// The engine's name hash (CStringID): standard CRC-32 of the name's bytes, case-sensitive.
constexpr uint32_t Crc32(std::string_view text) {
    uint32_t crc = 0xFFFFFFFFu;
    for (char c : text) {
        crc ^= static_cast<uint8_t>(c);
        for (int bit = 0; bit < 8; ++bit) {
            crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1u)));
        }
    }
    return ~crc;
}

}
