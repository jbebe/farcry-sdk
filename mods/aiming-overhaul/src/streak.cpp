// A streak's draw is the one made with the tracer texture bound: the very Direct3D texture the
// engine's resource for it holds, read from a trace as it is built, and the device's draws are
// watched for it at stage 0. Such a draw is made with the plugin's pixel shader in place of the
// engine's, for that draw alone, while the bound vertex shader is one of the engine's primitive
// permutations that hands the shader what it reads.
//
// See docs/docs/engine-internals/bullet-tracers.md.
#include "streak.h"

#include "engine/com.h"
#include "engine/shader.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include "tracer_ps.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <vector>

namespace {
    using SetTextureFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, DWORD stage,
                                             IDirect3DBaseTexture9* texture);
    using DrawPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                UINT start, UINT count);
    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9* device,
                                                       D3DPRIMITIVETYPE type, INT baseVertex,
                                                       UINT minVertex, UINT vertices,
                                                       UINT startIndex, UINT count);
    using DrawPrimitiveUPFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                  UINT count, const void* data, UINT stride);
    using DrawIndexedPrimitiveUPFn = HRESULT(__stdcall*)(IDirect3DDevice9* device,
                                                         D3DPRIMITIVETYPE type, UINT minVertex,
                                                         UINT vertices, UINT count,
                                                         const void* indices, D3DFORMAT format,
                                                         const void* data, UINT stride);

    SetTextureFn g_originalSetTexture = nullptr;
    DrawPrimitiveFn g_originalDrawPrimitive = nullptr;
    DrawIndexedPrimitiveFn g_originalDrawIndexedPrimitive = nullptr;
    DrawPrimitiveUPFn g_originalDrawPrimitiveUP = nullptr;
    DrawIndexedPrimitiveUPFn g_originalDrawIndexedPrimitiveUP = nullptr;

    // CTextureResource: the texture it hands the renderer; that one's device texture, a
    // CTextureD3D9; and that one's Direct3D texture.
    constexpr ptrdiff_t kResourceTexture = 0x28;
    constexpr ptrdiff_t kTextureDevice = 0x0C;
    constexpr ptrdiff_t kDeviceDirect3D = 0x18;

    // The CRC-32s of the bytecode of the primitive vertex shaders that hand on the vertex colour in
    // TEXCOORD0 and the texture coordinate in TEXCOORD1: the world one, and the screen one.
    constexpr uint32_t kReadableVertexShaders[] = {0xD3DCC958, 0x397122A1};

    std::atomic<IDirect3DBaseTexture9*> g_texture{nullptr};
    std::atomic<bool> g_enabled{true};
    AimingOverhaul::PixelShader g_shader{"tracer", g_tracerPixelShader};

    // What stage 0 holds, the vertex shader last asked about and whether it is readable, and
    // whether a streak has been drawn yet; all on the render thread.
    IDirect3DBaseTexture9* g_stage0 = nullptr;
    IDirect3DVertexShader9* g_vertexShader = nullptr;
    bool g_readable = false;
    bool g_drawnOne = false;

    // Null where the engine's pointers do not hold.
    IDirect3DBaseTexture9* Direct3DTextureOf(const uint8_t* resource) {
        __try {
            const uint8_t* texture = *reinterpret_cast<uint8_t* const*>(resource + kResourceTexture);
            const uint8_t* device = *reinterpret_cast<uint8_t* const*>(texture + kTextureDevice);
            return *reinterpret_cast<IDirect3DBaseTexture9* const*>(device + kDeviceDirect3D);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return nullptr;
        }
    }

    uint32_t Crc32(const uint8_t* data, size_t size) {
        uint32_t crc = 0xFFFFFFFF;
        for (size_t i = 0; i < size; i++) {
            crc ^= data[i];
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc >> 1) ^ (0xEDB88320 & (0u - (crc & 1)));
            }
        }
        return ~crc;
    }

    bool Readable(IDirect3DVertexShader9* shader) {
        if (shader == g_vertexShader) {
            return g_readable;
        }
        g_vertexShader = shader;
        g_readable = false;
        UINT size = 0;
        if (shader == nullptr || FAILED(shader->GetFunction(nullptr, &size)) || size == 0) {
            return false;
        }
        std::vector<uint8_t> bytecode(size);
        if (FAILED(shader->GetFunction(bytecode.data(), &size))) {
            return false;
        }
        g_readable = std::ranges::find(kReadableVertexShaders, Crc32(bytecode.data(), size)) !=
                     std::end(kReadableVertexShaders);
        return g_readable;
    }

    // Makes the draw with the plugin's shader if it is a streak's.
    template <class Draw>
    HRESULT Drawn(IDirect3DDevice9* device, Draw draw) {
        if (!g_enabled || g_stage0 == nullptr || g_stage0 != g_texture.load()) {
            return draw();
        }
        IDirect3DVertexShader9* vertexShader = nullptr;
        device->GetVertexShader(&vertexShader);
        const bool readable = Readable(AimingOverhaul::Borrowed(vertexShader));
        IDirect3DPixelShader9* shader = readable ? g_shader.Get(device) : nullptr;
        IDirect3DPixelShader9* engine = nullptr;
        if (shader == nullptr || FAILED(device->GetPixelShader(&engine))) {
            return draw();
        }
        device->SetPixelShader(shader);
        const HRESULT result = draw();
        device->SetPixelShader(engine);
        AimingOverhaul::Release(engine);
        if (!g_drawnOne) {
            g_drawnOne = true;
            FCSE::Logf("streak: drawing tracers with the plugin's shader");
        }
        return result;
    }

    HRESULT __stdcall SetTextureDetour(IDirect3DDevice9* device, DWORD stage,
                                       IDirect3DBaseTexture9* texture) {
        if (stage == 0) {
            g_stage0 = texture;
        }
        return g_originalSetTexture(device, stage, texture);
    }

    HRESULT __stdcall DrawPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                          UINT start, UINT count) {
        return Drawn(device, [&] { return g_originalDrawPrimitive(device, type, start, count); });
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertex, UINT minVertex, UINT vertices,
                                                 UINT startIndex, UINT count) {
        return Drawn(device, [&] {
            return g_originalDrawIndexedPrimitive(device, type, baseVertex, minVertex, vertices,
                                                  startIndex, count);
        });
    }

    HRESULT __stdcall DrawPrimitiveUPDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                            UINT count, const void* data, UINT stride) {
        return Drawn(device, [&] {
            return g_originalDrawPrimitiveUP(device, type, count, data, stride);
        });
    }

    HRESULT __stdcall DrawIndexedPrimitiveUPDetour(IDirect3DDevice9* device,
                                                   D3DPRIMITIVETYPE type, UINT minVertex,
                                                   UINT vertices, UINT count, const void* indices,
                                                   D3DFORMAT format, const void* data,
                                                   UINT stride) {
        return Drawn(device, [&] {
            return g_originalDrawIndexedPrimitiveUP(device, type, minVertex, vertices, count,
                                                    indices, format, data, stride);
        });
    }

    template <class Fn>
    bool HookSlot(size_t slot, Fn detour, Fn* original) {
        return AimingOverhaul::Vtable::Hook(slot, reinterpret_cast<void*>(detour),
                                            reinterpret_cast<void**>(original));
    }
}

bool AimingOverhaul::Streak::Install() {
    using namespace AimingOverhaul::Vtable;
    const bool hooked =
        HookSlot(kDrawPrimitive, &DrawPrimitiveDetour, &g_originalDrawPrimitive) &&
        HookSlot(kDrawIndexedPrimitive, &DrawIndexedPrimitiveDetour,
                 &g_originalDrawIndexedPrimitive) &&
        HookSlot(kDrawPrimitiveUP, &DrawPrimitiveUPDetour, &g_originalDrawPrimitiveUP) &&
        HookSlot(kDrawIndexedPrimitiveUP, &DrawIndexedPrimitiveUPDetour,
                 &g_originalDrawIndexedPrimitiveUP) &&
        HookSlot(kSetTexture, &SetTextureDetour, &g_originalSetTexture);
    if (!hooked) {
        FCSE::Logf("streak: the device cannot be hooked, so tracers are drawn with their texture");
    }
    return hooked;
}

void AimingOverhaul::Streak::FollowTexture(const uint8_t* resource) {
    if (resource != nullptr) {
        g_texture = Direct3DTextureOf(resource);
    }
}

void AimingOverhaul::Streak::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void AimingOverhaul::Streak::ReleaseDeviceObjects() {
    g_shader.Release();
    g_vertexShader = nullptr;
    g_stage0 = nullptr;
}
