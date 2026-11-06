#include "engine/scene_depth.h"

#include "engine/com.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <algorithm>
#include <iterator>
#include <unordered_map>
#include <vector>

namespace {
    constexpr size_t kResetSlot = 16;
    constexpr size_t kDrawPrimitiveSlot = 81;
    constexpr size_t kDrawIndexedPrimitiveSlot = 82;

    // The CRC-32 of every shipped D3D9 pixel shader that binds DepthVPSampler at s0, sorted.
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

    using ResetFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters);
    using DrawFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRIMITIVETYPE type, UINT start,
                                       UINT count);
    using DrawIndexedFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                              INT base, UINT lowest, UINT vertices, UINT start,
                                              UINT count);
    ResetFn g_originalReset = nullptr;
    DrawFn g_originalDraw = nullptr;
    DrawIndexedFn g_originalDrawIndexed = nullptr;

    // Render thread: the texture, with a reference of ours, and whether each pixel shader seen reads
    // it, by address.
    IDirect3DTexture9* g_texture = nullptr;
    std::unordered_map<IDirect3DPixelShader9*, bool> g_readers;

    bool ReadsDepth(IDirect3DPixelShader9* shader) {
        const auto [entry, added] = g_readers.try_emplace(shader, false);
        UINT size = 0;
        if (added && SUCCEEDED(shader->GetFunction(nullptr, &size)) && size > 0) {
            std::vector<DWORD> tokens(size / sizeof(DWORD));
            if (SUCCEEDED(shader->GetFunction(tokens.data(), &size))) {
                entry->second = std::binary_search(std::begin(kDepthReaders), std::end(kDepthReaders),
                                                   FCSE::Crc32(tokens.data(), size));
            }
        }
        return entry->second;
    }

    // A render target texture of the back buffer's size, as the scene's linear depth is.
    bool IsSceneDepth(IDirect3DDevice9* device, IDirect3DTexture9* texture) {
        D3DSURFACE_DESC desc = {};
        D3DSURFACE_DESC screen = {};
        PlayerBody::Surface backBuffer;
        return SUCCEEDED(texture->GetLevelDesc(0, &desc)) &&
               SUCCEEDED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) &&
               SUCCEEDED(backBuffer.surface->GetDesc(&screen)) && desc.Format == D3DFMT_A8R8G8B8 &&
               (desc.Usage & D3DUSAGE_RENDERTARGET) != 0 && desc.Width == screen.Width &&
               desc.Height == screen.Height;
    }

    void Look(IDirect3DDevice9* device) {
        IDirect3DPixelShader9* shader = nullptr;
        if (g_texture != nullptr || FAILED(device->GetPixelShader(&shader)) || shader == nullptr) {
            return;
        }
        const bool reads = ReadsDepth(shader);
        shader->Release();
        IDirect3DBaseTexture9* bound = nullptr;
        if (!reads || FAILED(device->GetTexture(0, &bound)) || bound == nullptr) {
            return;
        }
        auto* texture = static_cast<IDirect3DTexture9*>(bound);
        if (bound->GetType() == D3DRTYPE_TEXTURE && IsSceneDepth(device, texture)) {
            g_texture = texture;
            g_readers.clear();
        } else {
            bound->Release();
        }
    }

    HRESULT __stdcall ResetDetour(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters) {
        PlayerBody::Release(g_texture);
        g_readers.clear();
        return g_originalReset(device, parameters);
    }

    HRESULT __stdcall DrawDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type, UINT start,
                                 UINT count) {
        Look(device);
        return g_originalDraw(device, type, start, count);
    }

    HRESULT __stdcall DrawIndexedDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type, INT base,
                                        UINT lowest, UINT vertices, UINT start, UINT count) {
        Look(device);
        return g_originalDrawIndexed(device, type, base, lowest, vertices, start, count);
    }
}

bool PlayerBody::SceneDepth::Install() {
    return Vtable::Hook(kResetSlot, &ResetDetour, &g_originalReset) &&
           Vtable::Hook(kDrawPrimitiveSlot, &DrawDetour, &g_originalDraw) &&
           Vtable::Hook(kDrawIndexedPrimitiveSlot, &DrawIndexedDetour, &g_originalDrawIndexed);
}

IDirect3DTexture9* PlayerBody::SceneDepth::Texture() {
    return g_texture;
}