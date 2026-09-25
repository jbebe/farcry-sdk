#include "rocks.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/com.h"
#include "engine/crc32.h"
#include "engine/dome_draw.h"
#include "engine/known_shaders.h"
#include "engine/known_textures.h"
#include "engine/shader.h"
#include "engine/vertex_patch.h"
#include "fcse_api.h"
#include "tuning.h"

#include "rocks_cascaded_ps.h"
#include "rocks_river_ps.h"
#include "rocks_shadowless_ps.h"
#include "rocks_single_ps.h"
#include "rocks_vertex_ambient_ps.h"

#include <windows.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <iterator>
#include <string>
#include <vector>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
    using SkyOverhaul::DomeDraw::RockDraw;

    // A Generic pixel shader rock is drawn through, and ours that draws it instead.
    struct Variant {
        uint32_t crc;
        DWORD normalSampler;
        // The interpolator a patched vertex shader hands the camera-relative position through: one
        // the engine's shader leaves free, or its tangent-space half vector, which ours never reads.
        UINT positionSemantic;
        SkyOverhaul::PixelShader ours;
    };

    Variant g_variants[] = {
        // Cascaded shadows, with the ground colour from the hemisphere map.
        {0xADC8169F, 3, 8, {"rocks cascaded", g_rocksCascadedPixelShader}},
        // One shadow map slice.
        {0x769E50D2, 3, 8, {"rocks single", g_rocksSinglePixelShader}},
        // One slice, with the ambient lit per vertex: instanced rock at a distance.
        {0xDF9DCD60, 3, 7, {"rocks vertex ambient", g_rocksVertexAmbientPixelShader}},
        // No second diffuse map, so the normal map is at s2 and the shadow map at s3.
        {0x5E0DBF56, 2, 8, {"rocks river", g_rocksRiverPixelShader}},
        // No shadow map, past the shadows' reach.
        {0xAB2A1767, 3, 9, {"rocks shadowless", g_rocksShadowlessPixelShader}},
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

    // An engine vertex shader, held so its address cannot be reused, with ours made from it for
    // one position interpolator and where its light is; ours is null if either could not be had.
    struct Patched {
        IDirect3DVertexShader9* engine;
        UINT semantic;
        IDirect3DVertexShader9* ours;
        UINT light;
    };
    std::vector<Patched> g_patched;

    // How much light bare stone reflects straight back, whatever the material's own specular
    // colours say; about what the engine gives the desert's.
    constexpr float kStoneReflectance = 0.05f;

    // Our registers, in the order rocks.fx declares them from c110.
    enum Register { kCamera, kToLight, kShading, kDetail };

    // The detail map, built into the plugin by sky_overhaul.rc.
    IDirect3DTexture9* g_detail = nullptr;
    bool g_detailRefused = false;

    // The detail map's DDS header: where its size, its mip count and its bits per texel are.
    constexpr size_t kDdsHeight = 12;
    constexpr size_t kDdsWidth = 16;
    constexpr size_t kDdsLevels = 28;
    constexpr size_t kDdsBitsPerTexel = 88;
    constexpr size_t kDdsTexels = 128;

    // Rock draws since the last heartbeat, by variant.
    uint32_t g_drawn[std::size(g_variants)] = {};
    bool g_enabled = false;

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{10.0f};

    Variant* VariantOf(uint32_t pixel) {
        const auto found =
            std::find_if(std::begin(g_variants), std::end(g_variants),
                         [pixel](const Variant& variant) { return variant.crc == pixel; });
        return found != std::end(g_variants) ? found : nullptr;
    }

    bool IsRockAt(IDirect3DDevice9* device, DWORD sampler) {
        IDirect3DBaseTexture9* texture = nullptr;
        if (FAILED(device->GetTexture(sampler, &texture)) || texture == nullptr) {
            return false;
        }
        const bool rock = SkyOverhaul::KnownTextures::IsRockNormal(texture);
        texture->Release();
        return rock;
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
        if (found == nullptr) {
            return false;
        }
        const auto* bytes = static_cast<const uint8_t*>(LockResource(LoadResource(module, found)));
        const size_t size = SizeofResource(module, found);
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
            const size_t row = size_t{(std::max)(width >> level, 1u)} * 4;
            const UINT rows = (std::max)(height >> level, 1u);
            D3DLOCKED_RECT rect = {};
            if (offset + row * rows > size ||
                FAILED(g_detail->LockRect(level, &rect, nullptr, 0))) {
                SkyOverhaul::Release(g_detail);
                return false;
            }
            for (UINT y = 0; y < rows; y++) {
                std::memcpy(static_cast<uint8_t*>(rect.pBits) + size_t{y} * rect.Pitch,
                            bytes + offset + y * row, row);
            }
            g_detail->UnlockRect(level);
            offset += row * rows;
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
    // per engine shader.
    Patched PatchedVertex(IDirect3DDevice9* device, UINT semantic) {
        IDirect3DVertexShader9* engine = nullptr;
        if (FAILED(device->GetVertexShader(&engine)) || engine == nullptr) {
            return {};
        }
        for (const Patched& patched : g_patched) {
            if (patched.engine == engine && patched.semantic == semantic) {
                engine->Release();
                return patched;
            }
        }

        Patched patched = {engine, semantic, nullptr, 0};
        const std::vector<DWORD> tokens = SkyOverhaul::Bytecode(engine);
        const uint32_t crc = SkyOverhaul::Crc32(tokens.data(), tokens.size() * sizeof(DWORD));
        const auto light = std::find_if(
            std::begin(kLightRegisters), std::end(kLightRegisters),
            [crc](const LightRegister& entry) { return entry.vertex == crc; });
        const std::vector<DWORD> ours =
            SkyOverhaul::VertexPatch::WithPosition(tokens.data(), tokens.size(), semantic);
        if (light == std::end(kLightRegisters)) {
            FCSE::Logf("rocks: vertex shader %08X keeps its light where we do not know, so its "
                       "rock is left the engine's",
                       crc);
        } else if (ours.empty() || FAILED(device->CreateVertexShader(ours.data(), &patched.ours))) {
            FCSE::Logf("rocks: vertex shader %08X could not be made to hand on its position", crc);
            patched.ours = nullptr;
        } else {
            patched.light = light->reg;
            FCSE::Logf("rocks: vertex shader %08X hands on its position through TEXCOORD%u", crc,
                       semantic);
        }
        g_patched.push_back(patched);
        return patched;
    }

    bool Draw(IDirect3DDevice9* device, RockDraw& draw) {
        Variant* variant = VariantOf(SkyOverhaul::KnownShaders::Bound(device).crc);
        if (variant == nullptr || !IsRockAt(device, variant->normalSampler)) {
            return false;
        }
        draw.pixel = variant->ours.Get(device);
        if (draw.pixel == nullptr) {
            return false;
        }
        const Patched patched = PatchedVertex(device, variant->positionSemantic);
        if (patched.ours == nullptr) {
            return false;
        }
        draw.vertex = patched.ours;

        float* toLight = draw.parameters + kToLight * 4;
        device->GetVertexShaderConstantF(SkyOverhaul::Camera::kPositionRegister,
                                         draw.parameters + kCamera * 4, 1);
        device->GetVertexShaderConstantF(patched.light, toLight, 1);
        const float length =
            std::sqrt(toLight[0] * toLight[0] + toLight[1] * toLight[1] + toLight[2] * toLight[2]);
        for (int i = 0; i < 3; i++) {
            toLight[i] = length > 0.0f ? -toLight[i] / length : 0.0f;
        }

        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        float* shading = draw.parameters + kShading * 4;
        shading[0] = v.rockSmoothness;
        shading[1] = v.rockGlint * kStoneReflectance * (1.0f - v.rockSmoothness);
        shading[2] = v.rockGlintSharpness;
        draw.texture = DetailMap(device);
        if (draw.texture != nullptr) {
            float* detail = draw.parameters + kDetail * 4;
            detail[0] = 1.0f / v.rockDetailSize;
            detail[1] = v.rockDetailRelief;
            detail[2] = v.rockDetailGrain;
            detail[3] = v.rockCavity;
        }
        g_drawn[variant - g_variants]++;
        return true;
    }
}

void SkyOverhaul::Rocks::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetRocks(enabled ? &Draw : nullptr);
}

void SkyOverhaul::Rocks::OnFinalPass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    std::string line;
    for (size_t i = 0; i < std::size(g_variants); i++) {
        char text[32] = {};
        sprintf_s(text, " %08X %u,", g_variants[i].crc, g_drawn[i]);
        line += text;
    }
    FCSE::Logf("rocks: draws by shader%s %zu vertex shaders seen", line.c_str(), g_patched.size());
    std::fill(std::begin(g_drawn), std::end(g_drawn), 0u);
}

void SkyOverhaul::Rocks::ReleaseDeviceObjects() {
    SkyOverhaul::Release(g_detail);
    g_detailRefused = false;
    for (Variant& variant : g_variants) {
        variant.ours.Release();
    }
    for (Patched& patched : g_patched) {
        SkyOverhaul::Release(patched.ours);
        SkyOverhaul::Release(patched.engine);
    }
    g_patched.clear();
}
