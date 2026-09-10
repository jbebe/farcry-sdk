#include "engine/draw_census.h"

#include "engine/dome_draw.h"
#include "engine/log.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <cstdio>
#include <vector>
#include <windows.h>

namespace {
    constexpr size_t kDrawPrimitiveSlot = 81;

    // How long between measured frames. Uncapped, so the frames that matter - the ones the player
    // is looking at the problem in - are measured as surely as the first.
    constexpr float kCensusSeconds = 15.0f;

    // How much of one frame is kept. Past these the draws are still counted, so a limit that turns
    // out too small shows up as a number rather than as silence.
    constexpr size_t kMaxPasses = 24;
    constexpr size_t kMaxSignatures = 96;
    constexpr size_t kMaxShaders = 512;

    using DrawPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, UINT, UINT);
    DrawPrimitiveFn g_originalDrawPrimitive = nullptr;

    // Each shader object is read back once and remembered by address, since a frame binds the same
    // few hundred objects thousands of times.
    struct Shader {
        void* object;
        UINT size;
        unsigned long crc;
    };
    Shader g_shaders[kMaxShaders];
    size_t g_shaderCount = 0;

    // One kind of draw within one pass, and how many times the frame made it.
    struct Signature {
        UINT pass;
        bool indexed;
        D3DPRIMITIVETYPE type;
        UINT vertices;
        UINT primitives;
        UINT stride;
        DWORD zWrite;
        DWORD zFunc;
        float minZ;
        float maxZ;
        UINT psSize;
        unsigned long psCrc;
        UINT vsSize;
        unsigned long vsCrc;
        UINT textureWidth;
        UINT textureHeight;
        D3DFORMAT textureFormat;
        UINT count;
    };
    Signature g_signatures[kMaxSignatures];
    size_t g_signatureCount = 0;
    UINT g_notKept = 0;

    struct Pass {
        bool ended;
        bool scene;
        bool sky;
        float minZ;
        UINT draws;
        UINT blended;
    };
    Pass g_passes[kMaxPasses];

    bool g_open = false;
    uint32_t g_startSerial = 0;

    LARGE_INTEGER g_tickFrequency = {};
    LARGE_INTEGER g_lastTick = {};
    float g_sinceCensus = 0.0f;

    float FrameSeconds() {
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        if (g_tickFrequency.QuadPart == 0 || g_lastTick.QuadPart == 0) {
            g_lastTick = now;
            return 0.0f;
        }
        const float seconds = static_cast<float>(now.QuadPart - g_lastTick.QuadPart) /
                              static_cast<float>(g_tickFrequency.QuadPart);
        g_lastTick = now;
        return seconds < 0.0f ? 0.0f : seconds;
    }

    // Plain CRC-32, the checksum the archive's objects are compared by offline.
    unsigned long Crc32(const BYTE* data, UINT size) {
        unsigned long crc = 0xFFFFFFFFul;
        for (UINT i = 0; i < size; i++) {
            crc ^= data[i];
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc >> 1) ^ (0xEDB88320ul & (0ul - (crc & 1ul)));
            }
        }
        return ~crc & 0xFFFFFFFFul;
    }

    template <class ShaderT>
    void Measure(ShaderT* shader, UINT& size, unsigned long& crc) {
        size = 0;
        crc = 0;
        if (shader == nullptr) {
            return;
        }
        for (size_t i = 0; i < g_shaderCount; i++) {
            if (g_shaders[i].object == shader) {
                size = g_shaders[i].size;
                crc = g_shaders[i].crc;
                return;
            }
        }
        UINT length = 0;
        if (FAILED(shader->GetFunction(nullptr, &length)) || length == 0) {
            return;
        }
        std::vector<BYTE> code(length);
        if (FAILED(shader->GetFunction(code.data(), &length))) {
            return;
        }
        size = length;
        crc = Crc32(code.data(), length);
        if (g_shaderCount < kMaxShaders) {
            g_shaders[g_shaderCount++] = Shader{shader, size, crc};
        }
    }

    const char* PrimitiveName(D3DPRIMITIVETYPE type) {
        switch (type) {
            case D3DPT_POINTLIST: return "points";
            case D3DPT_LINELIST: return "lines";
            case D3DPT_LINESTRIP: return "linestrip";
            case D3DPT_TRIANGLELIST: return "trilist";
            case D3DPT_TRIANGLESTRIP: return "tristrip";
            case D3DPT_TRIANGLEFAN: return "trifan";
            default: return "?";
        }
    }

    // A texture format is either a four-character code or one of the enumerated values, and only
    // one of the two is readable: DXT5 rather than 894720068.
    void FormatName(D3DFORMAT format, char* out, size_t size) {
        const unsigned value = static_cast<unsigned>(format);
        const char code[4] = {static_cast<char>(value & 0xFF),
                              static_cast<char>((value >> 8) & 0xFF),
                              static_cast<char>((value >> 16) & 0xFF),
                              static_cast<char>((value >> 24) & 0xFF)};
        bool printable = true;
        for (size_t i = 0; i < sizeof(code); i++) {
            if (code[i] < 0x20 || code[i] > 0x7E) {
                printable = false;
            }
        }
        if (printable) {
            std::snprintf(out, size, "%.4s", code);
        } else {
            std::snprintf(out, size, "%u", value);
        }
    }

    void Observe(IDirect3DDevice9* device, bool indexed, D3DPRIMITIVETYPE type, UINT vertices,
                 UINT primitives) {
        if (!g_open) {
            return;
        }
        UINT pass = SkyOverhaul::Frame::PassSerial() - g_startSerial;
        if (pass >= kMaxPasses) {
            pass = kMaxPasses - 1;
        }
        g_passes[pass].draws++;

        // The fake terrain's technique blends exactly this way, and few other draws in a frame do,
        // which keeps a whole frame's worth of draws down to a list worth reading.
        DWORD blend = FALSE;
        device->GetRenderState(D3DRS_ALPHABLENDENABLE, &blend);
        if (!blend) {
            return;
        }
        DWORD source = 0;
        DWORD destination = 0;
        device->GetRenderState(D3DRS_SRCBLEND, &source);
        device->GetRenderState(D3DRS_DESTBLEND, &destination);
        if (source != D3DBLEND_SRCALPHA || destination != D3DBLEND_INVSRCALPHA) {
            return;
        }
        g_passes[pass].blended++;

        Signature seen = {};
        seen.pass = pass;
        seen.indexed = indexed;
        seen.type = type;
        seen.vertices = vertices;
        seen.primitives = primitives;

        IDirect3DVertexBuffer9* stream = nullptr;
        UINT offset = 0;
        if (SUCCEEDED(device->GetStreamSource(0, &stream, &offset, &seen.stride)) &&
            stream != nullptr) {
            stream->Release();
        }
        device->GetRenderState(D3DRS_ZWRITEENABLE, &seen.zWrite);
        device->GetRenderState(D3DRS_ZFUNC, &seen.zFunc);
        D3DVIEWPORT9 viewport = {};
        if (SUCCEEDED(device->GetViewport(&viewport))) {
            seen.minZ = viewport.MinZ;
            seen.maxZ = viewport.MaxZ;
        }

        IDirect3DPixelShader9* pixel = nullptr;
        if (SUCCEEDED(device->GetPixelShader(&pixel)) && pixel != nullptr) {
            Measure(pixel, seen.psSize, seen.psCrc);
            pixel->Release();
        }
        IDirect3DVertexShader9* vertex = nullptr;
        if (SUCCEEDED(device->GetVertexShader(&vertex)) && vertex != nullptr) {
            Measure(vertex, seen.vsSize, seen.vsCrc);
            vertex->Release();
        }

        IDirect3DBaseTexture9* texture = nullptr;
        if (SUCCEEDED(device->GetTexture(0, &texture)) && texture != nullptr) {
            if (texture->GetType() == D3DRTYPE_TEXTURE) {
                D3DSURFACE_DESC desc = {};
                if (SUCCEEDED(static_cast<IDirect3DTexture9*>(texture)->GetLevelDesc(0, &desc))) {
                    seen.textureWidth = desc.Width;
                    seen.textureHeight = desc.Height;
                    seen.textureFormat = desc.Format;
                }
            }
            texture->Release();
        }

        for (size_t i = 0; i < g_signatureCount; i++) {
            Signature& known = g_signatures[i];
            if (known.pass == seen.pass && known.indexed == seen.indexed &&
                known.type == seen.type && known.vertices == seen.vertices &&
                known.primitives == seen.primitives && known.stride == seen.stride &&
                known.psCrc == seen.psCrc && known.vsCrc == seen.vsCrc &&
                known.textureWidth == seen.textureWidth &&
                known.textureHeight == seen.textureHeight) {
                known.count++;
                return;
            }
        }
        if (g_signatureCount < kMaxSignatures) {
            seen.count = 1;
            g_signatures[g_signatureCount++] = seen;
        } else {
            g_notKept++;
        }
    }

    void ObserveIndexed(IDirect3DDevice9* device, D3DPRIMITIVETYPE type, UINT vertices,
                        UINT primitives) {
        Observe(device, true, type, vertices, primitives);
    }

    HRESULT __stdcall DrawPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                          UINT startVertex, UINT primitiveCount) {
        Observe(device, false, type, 0, primitiveCount);
        return g_originalDrawPrimitive(device, type, startVertex, primitiveCount);
    }

    void OpenWindow() {
        // The composite that just ended is this serial; everything after it belongs to the frame
        // being measured, up to and including the next composite.
        g_startSerial = SkyOverhaul::Frame::PassSerial() + 1;
        for (size_t i = 0; i < kMaxPasses; i++) {
            g_passes[i] = Pass{};
        }
        g_signatureCount = 0;
        g_notKept = 0;
        g_open = true;
    }

    void Dump(uint32_t frame) {
        UINT used = 0;
        for (size_t i = 0; i < kMaxPasses; i++) {
            if (g_passes[i].draws > 0 || g_passes[i].ended) {
                used = static_cast<UINT>(i) + 1;
            }
        }
        SkyOverhaul::Logf("census f%u: %u passes, %u blended draw kinds kept, %u not kept", frame,
                          used, static_cast<unsigned>(g_signatureCount), g_notKept);
        for (UINT i = 0; i < used; i++) {
            const Pass& pass = g_passes[i];
            SkyOverhaul::Logf("census f%u pass %u: %s draws %u, blended %u, viewport from %.3f", frame,
                              i, pass.sky ? "sky" : (pass.scene ? "scene" : "other"), pass.draws,
                              pass.blended, pass.minZ);
        }
        for (size_t i = 0; i < g_signatureCount; i++) {
            const Signature& s = g_signatures[i];
            char format[16] = "-";
            if (s.textureWidth != 0) {
                FormatName(s.textureFormat, format, sizeof(format));
            }
            SkyOverhaul::Logf("census f%u p%u: %s %s v=%u p=%u stride=%u x%u | zwrite %u zfunc %u "
                              "vp %.3f-%.3f",
                              frame, s.pass, s.indexed ? "dip" : "dp", PrimitiveName(s.type),
                              s.vertices, s.primitives, s.stride, s.count, s.zWrite, s.zFunc, s.minZ,
                              s.maxZ);
            SkyOverhaul::Logf("census f%u p%u:   ps %ub %08lx | vs %ub %08lx | t0 %ux%u %s", frame,
                              s.pass, s.psSize, s.psCrc, s.vsSize, s.vsCrc, s.textureWidth,
                              s.textureHeight, format);
        }
    }
}

bool SkyOverhaul::DrawCensus::Install() {
    QueryPerformanceFrequency(&g_tickFrequency);
    DomeDraw::SetObserver(&ObserveIndexed);

    // Losing the unindexed entry only narrows the census to indexed draws rather than ending it.
    void* plain = Vtable::Slot(kDrawPrimitiveSlot);
    if (plain == nullptr ||
        !FCSE::ApiPointer()->Hook(plain, reinterpret_cast<void*>(&DrawPrimitiveDetour),
                                  reinterpret_cast<void**>(&g_originalDrawPrimitive))) {
        Logf("census: DrawPrimitive could not be followed, so only indexed draws are measured");
    }
    Logf("census: measuring one frame's blended draws every %.0f seconds", kCensusSeconds);
    return true;
}

void SkyOverhaul::DrawCensus::OnScenePass(const Frame::Pass& pass) {
    if (!g_open) {
        return;
    }
    UINT index = Frame::PassSerial() - g_startSerial;
    if (index >= kMaxPasses) {
        return;
    }
    g_passes[index].ended = true;
    g_passes[index].scene = true;
    g_passes[index].sky = pass.sky;
    g_passes[index].minZ = pass.viewport.MinZ;
}

void SkyOverhaul::DrawCensus::OnFinalPass(const Frame::Pass& pass) {
    const float elapsed = FrameSeconds();
    if (g_open) {
        g_open = false;
        g_sinceCensus = 0.0f;
        Dump(pass.frame);
        return;
    }
    g_sinceCensus += elapsed;
    if (pass.live && g_sinceCensus >= kCensusSeconds) {
        OpenWindow();
    }
}

void SkyOverhaul::DrawCensus::ReleaseDeviceObjects() {
    g_shaderCount = 0;
}
