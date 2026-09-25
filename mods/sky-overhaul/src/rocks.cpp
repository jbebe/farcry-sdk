#include "rocks.h"

#include "engine/clock.h"
#include "engine/com.h"
#include "engine/dome_draw.h"
#include "engine/known_shaders.h"
#include "engine/known_textures.h"
#include "engine/shader.h"
#include "engine/vertex_patch.h"
#include "fcse_api.h"

#include "tuning.h"

#include "rocks_cascaded_parity_ps.h"
#include "rocks_cascaded_ps.h"
#include "rocks_census_ps.h"
#include "rocks_grid7_ps.h"
#include "rocks_grid8_ps.h"
#include "rocks_grid9_ps.h"
#include "rocks_river_parity_ps.h"
#include "rocks_river_ps.h"
#include "rocks_shadowless_parity_ps.h"
#include "rocks_shadowless_ps.h"
#include "rocks_single_parity_ps.h"
#include "rocks_single_ps.h"
#include "rocks_vertex_ambient_parity_ps.h"
#include "rocks_vertex_ambient_ps.h"

#include <windows.h>

#include <algorithm>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
    using SkyOverhaul::DomeDraw::RockDraw;
    using SkyOverhaul::Rocks::Mode;

    Mode g_mode = Mode::Engine;

    // What a Generic pixel shader drawing rock reads, as far as ours has to match it.
    enum class Layout {
        // Cascaded or single slice shadows, with the ground colour from the hemisphere map.
        Cascaded,
        Single,
        // Single slice shadows, with the ambient lit per vertex.
        VertexAmbient,
        // No second diffuse map, so the normal map is at s2 and the shadow map at s3.
        River,
        // No shadow map.
        Shadowless,
    };

    struct Variant {
        uint32_t crc;
        Layout layout;
        DWORD normalSampler;
        // The interpolator a patched vertex shader hands the camera-relative position through: one
        // the engine's shader leaves free, or its tangent-space half vector, which ours never reads.
        UINT positionSemantic;
    };

    // The engine's Generic pixel shaders that rock has been seen drawn through.
    constexpr Variant kVariants[] = {
        {0xADC8169F, Layout::Cascaded, 3, 8},     {0x769E50D2, Layout::Single, 3, 8},
        {0xDF9DCD60, Layout::VertexAmbient, 3, 7}, {0x5E0DBF56, Layout::River, 2, 8},
        {0xAB2A1767, Layout::Shadowless, 3, 9},
    };

    // Where each of the engine's vertex shaders feeding them keeps the direction its light travels.
    struct LightRegister {
        uint32_t vertex;
        UINT reg;
    };
    constexpr LightRegister kLightRegisters[] = {
        {0xC82830F5, 80}, {0x956A5009, 80},  {0xBB859448, 131}, {0x629398BF, 131},
        {0xE6833FBB, 76}, {0x17098736, 76},  {0xDA299D2C, 77},  {0x3B847C94, 77},
        {0x9445AF84, 128}, {0xFBC5C690, 73},
    };

    // By Layout.
    SkyOverhaul::PixelShader g_overhaul[] = {
        {"rocks cascaded", g_rocksCascadedPixelShader},
        {"rocks single", g_rocksSinglePixelShader},
        {"rocks vertex ambient", g_rocksVertexAmbientPixelShader},
        {"rocks river", g_rocksRiverPixelShader},
        {"rocks shadowless", g_rocksShadowlessPixelShader},
    };
    SkyOverhaul::PixelShader g_census{"rocks census", g_rocksCensusPixelShader};
    // By Layout.
    SkyOverhaul::PixelShader g_parity[] = {
        {"rocks cascaded parity", g_rocksCascadedParityPixelShader},
        {"rocks single parity", g_rocksSingleParityPixelShader},
        {"rocks vertex ambient parity", g_rocksVertexAmbientParityPixelShader},
        {"rocks river parity", g_rocksRiverParityPixelShader},
        {"rocks shadowless parity", g_rocksShadowlessParityPixelShader},
    };
    // By the interpolator the position arrives through, from TEXCOORD7.
    constexpr UINT kFirstPositionSemantic = 7;
    SkyOverhaul::PixelShader g_grid[] = {
        {"rocks grid 7", g_rocksGrid7PixelShader},
        {"rocks grid 8", g_rocksGrid8PixelShader},
        {"rocks grid 9", g_rocksGrid9PixelShader},
    };

    // The detail map, built into the plugin by sky_overhaul.rc.
    IDirect3DTexture9* g_detail = nullptr;
    bool g_detailRefused = false;

    // The detail map's DDS header: where its size, its mip count and its bits per texel are.
    constexpr size_t kDdsHeight = 12;
    constexpr size_t kDdsWidth = 16;
    constexpr size_t kDdsLevels = 28;
    constexpr size_t kDdsBitsPerTexel = 88;
    constexpr size_t kDdsTexels = 128;

    // An engine vertex shader, held so its address cannot be reused, and ours made from it.
    struct Patched {
        IDirect3DVertexShader9* engine;
        UINT semantic;
        IDirect3DVertexShader9* ours;
    };
    std::vector<Patched> g_patched;

    constexpr UINT kCameraPosition = 45;

    // The samplers a Generic pixel shader binds its maps, shadow map and hemisphere map to.
    constexpr DWORD kEngineSamplers = 6;

    // The registers a Generic pixel shader reads beyond the globals.
    constexpr UINT kFirstParameter = 71;
    constexpr UINT kParameterCount = 21;

    // Magenta, blue, green, orange and yellow, by Layout.
    constexpr float kTints[][4] = {
        {1, 0, 1, 1}, {0, 0.3f, 1, 1}, {0, 1, 0, 1}, {1, 0.5f, 0, 1}, {1, 1, 0, 1},
    };

    struct Pair {
        uint32_t pixel;
        uint32_t vertex;
        DWORD sampler;
        const char* map;

        bool operator==(const Pair&) const = default;
    };

    std::vector<Pair> g_pairs;
    // Rock draws since the last heartbeat, by Variant, then through any other shader.
    uint32_t g_drawn[std::size(kVariants) + 1] = {};

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{10.0f};

    const Variant* VariantOf(uint32_t pixel) {
        const auto found =
            std::find_if(std::begin(kVariants), std::end(kVariants),
                         [pixel](const Variant& variant) { return variant.crc == pixel; });
        return found != std::end(kVariants) ? found : nullptr;
    }

    // The rock normal map bound at `sampler`, or null.
    const char* RockAt(IDirect3DDevice9* device, DWORD sampler) {
        IDirect3DBaseTexture9* texture = nullptr;
        if (FAILED(device->GetTexture(sampler, &texture)) || texture == nullptr) {
            return nullptr;
        }
        const char* map = SkyOverhaul::KnownTextures::RockNormal(texture);
        texture->Release();
        return map;
    }

    template <class Shader>
    std::vector<DWORD> Bytecode(Shader* shader) {
        UINT size = 0;
        if (shader == nullptr || FAILED(shader->GetFunction(nullptr, &size)) || size == 0) {
            return {};
        }
        std::vector<DWORD> tokens(size / sizeof(DWORD));
        if (FAILED(shader->GetFunction(tokens.data(), &size))) {
            return {};
        }
        return tokens;
    }

    uint32_t Read32(const uint8_t* bytes, size_t offset) {
        uint32_t value = 0;
        std::memcpy(&value, bytes + offset, sizeof(value));
        return value;
    }

    // Uploads the embedded A8R8G8B8 DDS, whole mip chain, into a texture in the managed pool.
    bool UploadDetail(IDirect3DDevice9* device) {
        HMODULE module = reinterpret_cast<HMODULE>(&__ImageBase);
        HRSRC found = FindResource(module, TEXT("ROCK_DETAIL"), RT_RCDATA);
        HGLOBAL loaded = found != nullptr ? LoadResource(module, found) : nullptr;
        const auto* bytes = static_cast<const uint8_t*>(loaded != nullptr ? LockResource(loaded)
                                                                           : nullptr);
        const size_t size = found != nullptr ? SizeofResource(module, found) : 0;
        if (bytes == nullptr || size < kDdsTexels || std::memcmp(bytes, "DDS ", 4) != 0 ||
            Read32(bytes, kDdsBitsPerTexel) != 32) {
            return false;
        }
        const UINT width = Read32(bytes, kDdsWidth);
        const UINT height = Read32(bytes, kDdsHeight);
        const UINT levels = Read32(bytes, kDdsLevels);
        if (FAILED(device->CreateTexture(width, height, levels, 0, D3DFMT_A8R8G8B8,
                                         D3DPOOL_MANAGED, &g_detail, nullptr))) {
            return false;
        }
        size_t offset = kDdsTexels;
        for (UINT level = 0; level < levels; level++) {
            const UINT w = (std::max)(width >> level, 1u);
            const UINT h = (std::max)(height >> level, 1u);
            D3DLOCKED_RECT rect = {};
            if (offset + size_t{w} * h * 4 > size ||
                FAILED(g_detail->LockRect(level, &rect, nullptr, 0))) {
                SkyOverhaul::Release(g_detail);
                return false;
            }
            for (UINT y = 0; y < h; y++) {
                std::memcpy(static_cast<uint8_t*>(rect.pBits) + size_t{y} * rect.Pitch,
                            bytes + offset + size_t{y} * w * 4, size_t{w} * 4);
            }
            g_detail->UnlockRect(level);
            offset += size_t{w} * h * 4;
        }
        return true;
    }

    // The detail map, uploaded the first time it is asked for; null if the device refused it.
    IDirect3DTexture9* DetailMap(IDirect3DDevice9* device) {
        if (g_detail == nullptr && !g_detailRefused && !UploadDetail(device)) {
            g_detailRefused = true;
            FCSE::Logf("rocks: the detail map could not be made, so rock is drawn without it");
        }
        return g_detail;
    }

    // The bound vertex shader with the position handed on through TEXCOORD`semantic`, made once
    // per engine shader; null if it cannot be.
    IDirect3DVertexShader9* PatchedVertex(IDirect3DDevice9* device, UINT semantic) {
        IDirect3DVertexShader9* engine = nullptr;
        if (FAILED(device->GetVertexShader(&engine)) || engine == nullptr) {
            return nullptr;
        }
        for (const Patched& patched : g_patched) {
            if (patched.engine == engine && patched.semantic == semantic) {
                engine->Release();
                return patched.ours;
            }
        }

        Patched patched = {engine, semantic, nullptr};
        const std::vector<DWORD> tokens = Bytecode(engine);
        const std::vector<DWORD> ours =
            SkyOverhaul::VertexPatch::WithPosition(tokens.data(), tokens.size(), semantic);
        const uint32_t crc = SkyOverhaul::KnownShaders::VertexBound(device).crc;
        if (ours.empty() || FAILED(device->CreateVertexShader(ours.data(), &patched.ours))) {
            FCSE::Logf("rocks: vertex shader %08X could not be made to hand on its position", crc);
            patched.ours = nullptr;
        } else {
            FCSE::Logf("rocks: vertex shader %08X hands on its position through TEXCOORD%u", crc,
                       semantic);
        }
        g_patched.push_back(patched);
        return patched.ours;
    }

    const std::filesystem::path& DumpFolder() {
        static const std::filesystem::path folder = [] {
            std::wstring exe(32768, L'\0');
            exe.resize(GetModuleFileNameW(nullptr, exe.data(), static_cast<DWORD>(exe.size())));
            return std::filesystem::path(exe).parent_path() / L"sky-overhaul-census";
        }();
        return folder;
    }

    // Writes the bound shader's bytecode beside the game, named by its CRC.
    template <class Shader>
    void Dump(Shader* shader, uint32_t crc, const wchar_t* extension) {
        const std::vector<DWORD> tokens = Bytecode(shader);
        if (tokens.empty()) {
            return;
        }
        std::error_code error;
        std::filesystem::create_directories(DumpFolder(), error);
        wchar_t name[16] = {};
        swprintf_s(name, L"%08X%s", crc, extension);
        std::ofstream(DumpFolder() / name, std::ios::binary)
            .write(reinterpret_cast<const char*>(tokens.data()), tokens.size() * sizeof(DWORD));
    }

    // Everything about a new pair of shaders that the replacement will have to match.
    void Report(IDirect3DDevice9* device, const Pair& pair, bool known) {
        DWORD alphaTest = 0, cull = 0, zWrite = 0, colourWrite = 0, blend = 0;
        device->GetRenderState(D3DRS_ALPHATESTENABLE, &alphaTest);
        device->GetRenderState(D3DRS_CULLMODE, &cull);
        device->GetRenderState(D3DRS_ZWRITEENABLE, &zWrite);
        device->GetRenderState(D3DRS_COLORWRITEENABLE, &colourWrite);
        device->GetRenderState(D3DRS_ALPHABLENDENABLE, &blend);
        FCSE::Logf("rocks census: new pair ps %08X vs %08X (%s), %s at s%lu; alpha test %lu, "
                   "cull %lu, z write %lu, colour write %lX, blend %lu",
                   pair.pixel, pair.vertex, known ? "known" : "other", pair.map, pair.sampler,
                   alphaTest, cull, zWrite, colourWrite, blend);
        if (!known) {
            return;
        }

        IDirect3DVertexShader9* vertex = nullptr;
        device->GetVertexShader(&vertex);
        Dump(vertex, pair.vertex, L".vso");
        SkyOverhaul::Release(vertex);

        float parameters[kParameterCount * 4] = {};
        device->GetPixelShaderConstantF(kFirstParameter, parameters, kParameterCount);
        for (UINT first = 0; first < kParameterCount; first += 7) {
            std::string line;
            for (UINT r = first; r < (std::min)(first + 7, kParameterCount); r++) {
                char text[96] = {};
                sprintf_s(text, " c%u(%g %g %g %g)", kFirstParameter + r, parameters[r * 4],
                          parameters[r * 4 + 1], parameters[r * 4 + 2], parameters[r * 4 + 3]);
                line += text;
            }
            FCSE::Logf("rocks census:  %s", line.c_str());
        }
    }

    // Paints rock drawn through a known variant by its layout; rock maps bound to any other shader
    // are only logged, since most are left behind by an earlier draw rather than read.
    bool Census(IDirect3DDevice9* device, uint32_t pixel, const Variant* variant, RockDraw& draw) {
        Pair pair = {pixel};
        if (variant != nullptr) {
            pair.sampler = variant->normalSampler;
            pair.map = RockAt(device, pair.sampler);
        } else {
            for (DWORD sampler = 0; sampler < kEngineSamplers && pair.map == nullptr; sampler++) {
                pair.sampler = sampler;
                pair.map = RockAt(device, sampler);
            }
        }
        if (pair.map == nullptr) {
            return false;
        }

        pair.vertex = SkyOverhaul::KnownShaders::VertexBound(device).crc;
        if (std::find(g_pairs.begin(), g_pairs.end(), pair) == g_pairs.end()) {
            g_pairs.push_back(pair);
            Report(device, pair, variant != nullptr);
        }
        g_drawn[variant != nullptr ? variant - kVariants : std::size(kVariants)]++;
        if (variant == nullptr) {
            return false;
        }
        const float* colour = kTints[static_cast<int>(variant->layout)];
        std::copy(colour, colour + 4, draw.parameters);
        draw.pixel = g_census.Get(device);
        return draw.pixel != nullptr;
    }

    bool Draw(IDirect3DDevice9* device, RockDraw& draw) {
        const uint32_t pixel = SkyOverhaul::KnownShaders::Bound(device).crc;
        const Variant* variant = VariantOf(pixel);
        if (g_mode == Mode::Census) {
            return Census(device, pixel, variant, draw);
        }
        if (variant == nullptr || RockAt(device, variant->normalSampler) == nullptr) {
            return false;
        }

        switch (g_mode) {
        case Mode::Blink:
            if (GetTickCount() / 1000 % 2 == 0) {
                return false;
            }
            [[fallthrough]];
        case Mode::Parity:
            draw.pixel = g_parity[static_cast<int>(variant->layout)].Get(device);
            break;
        case Mode::Grid:
            draw.pixel = g_grid[variant->positionSemantic - kFirstPositionSemantic].Get(device);
            break;
        case Mode::Overhaul:
            draw.pixel = g_overhaul[static_cast<int>(variant->layout)].Get(device);
            break;
        default:
            return false;
        }
        if (g_mode == Mode::Grid || g_mode == Mode::Overhaul) {
            const uint32_t vertex = SkyOverhaul::KnownShaders::VertexBound(device).crc;
            const auto light = std::find_if(
                std::begin(kLightRegisters), std::end(kLightRegisters),
                [vertex](const LightRegister& entry) { return entry.vertex == vertex; });
            if (light == std::end(kLightRegisters)) {
                return false;
            }
            draw.vertex = PatchedVertex(device, variant->positionSemantic);
            device->GetVertexShaderConstantF(kCameraPosition, draw.parameters, 1);
            device->GetVertexShaderConstantF(light->reg, draw.parameters + 4, 1);
            const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
            draw.parameters[8] = v.rockSmoothness;
            draw.parameters[9] = v.rockGlint;
            draw.parameters[10] = v.rockGlintSharpness;
            draw.texture = DetailMap(device);
            if (draw.texture != nullptr) {
                draw.parameters[12] = 1.0f / v.rockDetailSize;
                draw.parameters[13] = v.rockDetailRelief;
                draw.parameters[14] = v.rockDetailGrain;
                draw.parameters[15] = v.rockCavity;
            }
            if (draw.vertex == nullptr) {
                return false;
            }
        }
        if (draw.pixel == nullptr) {
            return false;
        }
        g_drawn[variant - kVariants]++;
        return true;
    }
}

void SkyOverhaul::Rocks::SetMode(Mode mode) {
    g_mode = mode;
    DomeDraw::SetRocks(mode == Mode::Engine ? nullptr : &Draw);
}

void SkyOverhaul::Rocks::OnFinalPass(const Frame::Pass& pass) {
    if (g_mode == Mode::Engine || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    std::string line;
    for (size_t i = 0; i < std::size(kVariants); i++) {
        char text[32] = {};
        sprintf_s(text, " %08X %u,", kVariants[i].crc, g_drawn[i]);
        line += text;
    }
    FCSE::Logf("rocks: draws by shader%s other %u; %zu census pairs, %zu vertex shaders patched",
               line.c_str(), g_drawn[std::size(kVariants)], g_pairs.size(), g_patched.size());
    std::fill(std::begin(g_drawn), std::end(g_drawn), 0u);
}

void SkyOverhaul::Rocks::ReleaseDeviceObjects() {
    SkyOverhaul::Release(g_detail);
    g_detailRefused = false;
    for (SkyOverhaul::PixelShader& shader : g_overhaul) {
        shader.Release();
    }
    g_census.Release();
    for (SkyOverhaul::PixelShader& shader : g_parity) {
        shader.Release();
    }
    for (SkyOverhaul::PixelShader& shader : g_grid) {
        shader.Release();
    }
    for (Patched& patched : g_patched) {
        SkyOverhaul::Release(patched.ours);
        SkyOverhaul::Release(patched.engine);
    }
    g_patched.clear();
}
