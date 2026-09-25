// The CRC-32 of zlib, which is what names a shipped shader or texture.
#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

namespace SkyOverhaul {

namespace Detail {
    constexpr std::array<uint32_t, 256> kCrcTable = [] {
        std::array<uint32_t, 256> table = {};
        for (uint32_t i = 0; i < 256; i++) {
            uint32_t crc = i;
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1u)));
            }
            table[i] = crc;
        }
        return table;
    }();
}

// Continues `crc` over more bytes, so a CRC can be taken piece by piece.
inline uint32_t Crc32(const void* data, size_t size, uint32_t crc = 0) {
    const auto* bytes = static_cast<const uint8_t*>(data);
    crc = ~crc;
    for (size_t i = 0; i < size; i++) {
        crc = Detail::kCrcTable[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
    }
    return ~crc;
}

}
