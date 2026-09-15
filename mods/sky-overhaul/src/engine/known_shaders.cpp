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

    // Every shipped D3D9 vertex shader of grass or of tree leaves, sorted. What finds them is in
    // docs/docs/file-formats/shader-objects.md.
    constexpr uint32_t kFoliage[] = {
        0x0043D0E6, 0x0519078F, 0x079BE7DF, 0x08538957, 0x08C6194E, 0x09B2D42A, 0x0B870534,
        0x0DD82273, 0x0DFB2EFF, 0x18A9BB2E, 0x1BB579E6, 0x1D09DDA6, 0x20F4B2CD, 0x246BFB95,
        0x25D070CA, 0x2656EB5F, 0x27D71C8B, 0x291F4A6D, 0x2A6D3855, 0x2AF1C6EC, 0x3415446E,
        0x34D77A8E, 0x350228D5, 0x36C36CC9, 0x39966A6A, 0x3C5EB4D3, 0x48726345, 0x4A0F74AB,
        0x4BC7B0BE, 0x4CC3B1D9, 0x53E991E0, 0x53F16102, 0x5566D92B, 0x55D45943, 0x574275F4,
        0x59DCE49A, 0x5A52BBEA, 0x5C7B04D8, 0x6448EB4C, 0x64F4B38D, 0x654D3670, 0x66239B2C,
        0x6834F6F5, 0x6F201110, 0x71F3041E, 0x728597BB, 0x7285DBA7, 0x74CD44DB, 0x7542399D,
        0x778B495B, 0x77A85561, 0x7876600C, 0x7CECA2CD, 0x7F82D1AD, 0x7FD90F93, 0x800258F6,
        0x84420A76, 0x84FEBBB5, 0x850CBA72, 0x86D0461A, 0x87ED8CA1, 0x8852CC20, 0x8AB95562,
        0x8D845258, 0x8DFD9197, 0x8E104D41, 0x929FA362, 0x941925A9, 0x962D1F8A, 0x9641E083,
        0x96A4B50D, 0x971CA74B, 0x98AC462C, 0x9A147DDC, 0x9CE758D4, 0x9CF752BC, 0x9E25DC6E,
        0xA1C9D2BE, 0xA2B6C1DB, 0xA5009030, 0xA52D769A, 0xA65F1AB5, 0xAAA86F11, 0xAD8B7ED2,
        0xAE243C7D, 0xAE9280F4, 0xB0D96A44, 0xB5B07906, 0xB92B668F, 0xBA2AD356, 0xBB45ACA3,
        0xBBC5640F, 0xBCD02F53, 0xBD5FCF3D, 0xBDE7C303, 0xBEBCCB58, 0xBF35FE99, 0xC0E082D3,
        0xC58847FE, 0xC5906122, 0xC5E7D343, 0xC66B99F8, 0xC88EDC22, 0xC8F86122, 0xCA29FBA6,
        0xCCF18121, 0xCD4FD515, 0xCFFCFE03, 0xD015AA46, 0xD129B0B3, 0xD1479CCF, 0xD4234215,
        0xD5108DE5, 0xD51FDDE9, 0xD94ED10F, 0xD9786FB8, 0xDA569D77, 0xDA7558A7, 0xDD9E3DE4,
        0xE2891F7B, 0xE5BB8CF9, 0xEA24248E, 0xEABABAF2, 0xEC0AF2B2, 0xEE2F5E4A, 0xEE4FDBDC,
        0xF38A1B62, 0xF3A01445, 0xF457FA18, 0xF47ABFA5, 0xF600B584, 0xF77C3542, 0xF82E06DB,
        0xFB24621F, 0xFB76EC90, 0xFB8A9289,
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

    // What each shader seen was classified as, by its address. Consecutive draws mostly share a
    // shader, which spares the map.
    template <class Shader, class Value>
    struct Cache {
        std::unordered_map<Shader*, Value> known;
        Shader* last = nullptr;
        Value lastValue = {};

        template <class Classify>
        Value Get(Shader* shader, Classify classify) {
            if (shader != last) {
                const auto [found, inserted] = known.try_emplace(shader);
                if (inserted) {
                    found->second = classify(shader);
                }
                lastValue = found->second;
                last = shader;
            }
            return lastValue;
        }

        void Forget() {
            known.clear();
            last = nullptr;
        }
    };

    Cache<IDirect3DPixelShader9, Known> g_pixelShaders;
    Cache<IDirect3DVertexShader9, bool> g_foliage;

    // The CRC-32 of a shader's bytecode, or false if the device would not hand it over.
    template <class Shader>
    bool BytecodeCrc(Shader* shader, uint32_t& crc) {
        UINT size = 0;
        if (FAILED(shader->GetFunction(nullptr, &size)) || size == 0) {
            return false;
        }
        std::vector<uint8_t> bytecode(size);
        if (FAILED(shader->GetFunction(bytecode.data(), &size))) {
            return false;
        }
        crc = 0xFFFFFFFFu;
        for (uint8_t byte : bytecode) {
            crc = kCrcTable[(crc ^ byte) & 0xFF] ^ (crc >> 8);
        }
        crc = ~crc;
        return true;
    }

    Known Classify(IDirect3DPixelShader9* shader) {
        uint32_t crc = 0;
        if (!BytecodeCrc(shader, crc)) {
            return kOther;
        }
        if (std::binary_search(std::begin(kDepthReaders), std::end(kDepthReaders), crc)) {
            return {Kind::DepthReader, 0};
        }
        const auto named = std::find_if(std::begin(kNamed), std::end(kNamed),
                                        [crc](const Named& entry) { return entry.crc == crc; });
        return named != std::end(kNamed) ? named->known : kOther;
    }

    bool IsFoliage(IDirect3DVertexShader9* shader) {
        uint32_t crc = 0;
        return BytecodeCrc(shader, crc) &&
               std::binary_search(std::begin(kFoliage), std::end(kFoliage), crc);
    }
}

Known SkyOverhaul::KnownShaders::Bound(IDirect3DDevice9* device) {
    IDirect3DPixelShader9* shader = nullptr;
    if (FAILED(device->GetPixelShader(&shader)) || shader == nullptr) {
        return kOther;
    }
    // Only the address is kept, and a shader the engine still has bound is alive.
    shader->Release();
    return g_pixelShaders.Get(shader, Classify);
}

bool SkyOverhaul::KnownShaders::IsFoliageBound(IDirect3DDevice9* device) {
    IDirect3DVertexShader9* shader = nullptr;
    if (FAILED(device->GetVertexShader(&shader)) || shader == nullptr) {
        return false;
    }
    shader->Release();
    return g_foliage.Get(shader, IsFoliage);
}

void SkyOverhaul::KnownShaders::Forget() {
    g_pixelShaders.Forget();
    g_foliage.Forget();
}
