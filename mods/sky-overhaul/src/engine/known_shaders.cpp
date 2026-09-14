#include "engine/known_shaders.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <iterator>
#include <unordered_map>
#include <vector>

namespace {
    using SkyOverhaul::KnownShaders::Kind;
    using SkyOverhaul::KnownShaders::Known;

    struct Named {
        uint32_t crc;
        Known known;
    };

    // Every shipped D3D9 pixel shader that binds DepthVPSampler at s0, sorted.
    constexpr uint32_t kDepthReaders[] = {
        0x03D7976B, 0x04FC2DE3, 0x077933EA, 0x095348D1, 0x0A4D4E0A, 0x0D149BD2, 0x153D6420,
        0x18C0F095, 0x19C375B5, 0x1B5CBF6E, 0x1ED3AC0E, 0x20AAC3F4, 0x2385FF17, 0x25ABA1FB,
        0x261AC907, 0x27D4D946, 0x2C6D8523, 0x3166A598, 0x377F57F7, 0x3F765874, 0x41A48FD3,
        0x42B03599, 0x42C74CC8, 0x4C6A868A, 0x4E1620D5, 0x4E2319E4, 0x4E410723, 0x506AF199,
        0x508D532A, 0x58D493E4, 0x5BE1B5F9, 0x623041CA, 0x63EAD53F, 0x6E64F0DA, 0x7AE0646F,
        0x82194871, 0x82523DC8, 0x8297BACE, 0x90809C25, 0x9211CDFD, 0x96502125, 0x9D2A3147,
        0xA371A62F, 0xA4A517A6, 0xA692051E, 0xA84D5BC0, 0xA947D525, 0xAA8E9166, 0xACB4415E,
        0xAD23D599, 0xB5DC7FF0, 0xBBC94811, 0xBDD0CC35, 0xBFBEE18F, 0xBFFB1591, 0xC662DA13,
        0xCA3AF78E, 0xCF944BE0, 0xD0DA16BF, 0xD27B9B3C, 0xD2B42D82, 0xD39362B3, 0xD4A36FF2,
        0xE10A0560, 0xE23401B3, 0xE3374C89, 0xE783F237, 0xF0769B56, 0xF46D73C4, 0xF50A565E,
        0xF523027A, 0xF5D336B6, 0xF62621F1, 0xFC866E3A,
    };

    // The moon's two fogged CelestialBody objects, and the seven that bind the grade. The grades
    // that also adapt the bloom's luminance keep its parameters in the registers below.
    constexpr Named kNamed[] = {
        {0x6FE01C46, {Kind::Moon, 0}},   {0x544EE1EA, {Kind::Moon, 0}},
        {0x0B4D5D4B, {Kind::Grade, 71}}, {0xC8D0F940, {Kind::Grade, 71}},
        {0xD6B802C7, {Kind::Grade, 71}}, {0x8B52A2A0, {Kind::Grade, 73}},
        {0xBFEA914A, {Kind::Grade, 73}}, {0x00AF3B5D, {Kind::Grade, 73}},
        {0x682703CF, {Kind::Grade, 73}},
    };

    constexpr Known kOther = {Kind::Other, 0};

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

    std::unordered_map<IDirect3DPixelShader9*, Known> g_known;
    // Consecutive draws mostly share a shader, which spares the map.
    IDirect3DPixelShader9* g_lastShader = nullptr;
    Known g_last = kOther;

    uint32_t Crc32(const std::vector<uint8_t>& bytes) {
        uint32_t crc = 0xFFFFFFFFu;
        for (uint8_t byte : bytes) {
            crc = kCrcTable[(crc ^ byte) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    Known Classify(IDirect3DPixelShader9* shader) {
        UINT size = 0;
        if (FAILED(shader->GetFunction(nullptr, &size)) || size == 0) {
            return kOther;
        }
        std::vector<uint8_t> bytecode(size);
        if (FAILED(shader->GetFunction(bytecode.data(), &size))) {
            return kOther;
        }
        const uint32_t crc = Crc32(bytecode);
        if (std::binary_search(std::begin(kDepthReaders), std::end(kDepthReaders), crc)) {
            return {Kind::DepthReader, 0};
        }
        const auto named = std::find_if(std::begin(kNamed), std::end(kNamed),
                                        [crc](const Named& entry) { return entry.crc == crc; });
        return named != std::end(kNamed) ? named->known : kOther;
    }
}

Known SkyOverhaul::KnownShaders::Bound(IDirect3DDevice9* device) {
    IDirect3DPixelShader9* shader = nullptr;
    if (FAILED(device->GetPixelShader(&shader)) || shader == nullptr) {
        return kOther;
    }
    // Only the address is kept, and a shader the engine still has bound is alive.
    shader->Release();
    if (shader != g_lastShader) {
        const auto found = g_known.find(shader);
        g_last = found != g_known.end() ? found->second : g_known[shader] = Classify(shader);
        g_lastShader = shader;
    }
    return g_last;
}

void SkyOverhaul::KnownShaders::Forget() {
    g_known.clear();
    g_lastShader = nullptr;
}
