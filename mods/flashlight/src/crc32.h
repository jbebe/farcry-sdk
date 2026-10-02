// CRC32, the hash behind both a magma::Id and an action-map signal id.
#pragma once

#include <cstdint>

namespace Flashlight {

constexpr uint32_t Crc32(const char* text) {
    uint32_t crc = 0xFFFFFFFF;
    for (; *text != '\0'; ++text) {
        crc ^= static_cast<uint8_t>(*text);
        for (int bit = 0; bit < 8; ++bit) {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
    }
    return ~crc;
}

}
