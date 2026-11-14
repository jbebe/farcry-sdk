// A streak's draw, the one binding the engine's own tracer texture at stage 0, is made with the
// plugin's pixel shader while the bound vertex shader hands on what that reads.
// See docs/docs/engine-internals/bullet-tracers.md.
#include "streak.h"

#include "engine/com.h"
#include "engine/engine_device.h"
#include "engine/memory.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "tracer_ps.h"

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <vector>

namespace {
    // The engine device's plain draw, which every streak goes through; __thiscall declared
    // __fastcall, and `type` the engine's own primitive type, which only the engine reads.
    using DrawPrimitiveFn = HRESULT(__fastcall*)(void* self, void* unused, UINT type, UINT start,
                                                 UINT count);
    FCSE::Relocation<DrawPrimitiveFn> g_engineDraw{FCSE::Uplay(0x00423A70)};
    DrawPrimitiveFn g_original = nullptr;

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

    // The vertex shader last asked about and whether it is readable; on the render thread.
    IDirect3DVertexShader9* g_vertexShader = nullptr;
    bool g_readable = false;

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
        g_readable = std::ranges::find(kReadableVertexShaders, FCSE::Crc32(bytecode.data(), size)) !=
                     std::end(kReadableVertexShaders);
        return g_readable;
    }

    // Whether the draw about to be made is a streak's: the engine's tracer texture at stage 0, asked
    // first so the shader check, cached for one shader, only ever sees the streaks' own.
    bool IsStreak(IDirect3DDevice9* device, IDirect3DBaseTexture9* tracer) {
        IDirect3DBaseTexture9* stage0 = nullptr;
        device->GetTexture(0, &stage0);
        if (AimingOverhaul::Borrowed(stage0) != tracer) {
            return false;
        }
        IDirect3DVertexShader9* vertexShader = nullptr;
        device->GetVertexShader(&vertexShader);
        return Readable(AimingOverhaul::Borrowed(vertexShader));
    }

    HRESULT __fastcall DrawPrimitiveDetour(void* self, void* unused, UINT type, UINT start,
                                           UINT count) {
        const auto draw = [&] { return g_original(self, unused, type, start, count); };
        IDirect3DBaseTexture9* tracer = g_texture.load();
        if (!g_enabled || tracer == nullptr) {
            return draw();
        }
        IDirect3DDevice9* device = AimingOverhaul::EngineDevice::Direct3D(self);
        IDirect3DPixelShader9* shader = IsStreak(device, tracer) ? g_shader.Get(device) : nullptr;
        IDirect3DPixelShader9* engine = nullptr;
        if (shader == nullptr || FAILED(device->GetPixelShader(&engine))) {
            return draw();
        }
        device->SetPixelShader(shader);
        const HRESULT result = draw();
        device->SetPixelShader(engine);
        AimingOverhaul::Release(engine);
        return result;
    }

}

bool AimingOverhaul::Streak::Install() {
    const bool hooked = Hook(g_engineDraw.address(), &DrawPrimitiveDetour, &g_original);
    if (!hooked) {
        FCSE::Logf("streak: the engine's draw cannot be hooked; tracers keep their texture");
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
}
