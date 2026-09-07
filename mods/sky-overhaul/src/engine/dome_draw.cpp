#include "engine/dome_draw.h"

#include "engine/log.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <cstdio>
#include <windows.h>

namespace {
    constexpr size_t kDrawPrimitiveSlot = 81;
    constexpr size_t kDrawIndexedPrimitiveSlot = 82;

    // How long between measurements. Every draw in a measured pass asks the device about itself,
    // which is far more than a frame can afford continuously - so it is afforded once in a while.
    constexpr float kCensusSeconds = 5.0f;

    // How many of a pass's draws are described. A sky pass holds a handful; past this they are
    // still counted, so a wrong guess shows up as a count rather than as silence.
    constexpr size_t kMaxRecords = 24;

    // How many texture stages are described. The stormiest sky dome in the prototype reads four.
    constexpr DWORD kSamplers = 4;

    using DrawPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, UINT, UINT);
    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT,
                                                       UINT, UINT, UINT, UINT);

    DrawPrimitiveFn g_originalDrawPrimitive = nullptr;
    DrawIndexedPrimitiveFn g_originalDrawIndexedPrimitive = nullptr;

    // What one draw at the far end of the depth range was, and what the device was set to when it
    // happened. Every field is read at the draw itself: by the time the pass ends the state has
    // moved on, so none of this can be gathered afterwards.
    struct Record {
        bool indexed;
        D3DPRIMITIVETYPE type;
        UINT vertices;
        UINT primitives;
        UINT startIndex;
        UINT stride;
        float minZ;
        float maxZ;
        UINT targetWidth;
        UINT targetHeight;
        DWORD zEnable;
        DWORD zWrite;
        DWORD zFunc;
        DWORD cull;
        DWORD alphaBlend;
        DWORD srcBlend;
        DWORD destBlend;
        DWORD alphaTest;
        UINT shaderSize;
        UINT textureWidth[kSamplers];
        UINT textureHeight[kSamplers];
        D3DFORMAT textureFormat[kSamplers];
        // The registers the camera is carried in, read back to show they are already live at the
        // pass's first draw rather than only by the time the pass ends.
        float position[3];
        float direction[3];
    };

    Record g_records[kMaxRecords];
    size_t g_recorded = 0;

    bool g_measuring = false;
    UINT g_windowDraws = 0;
    UINT g_skyDraws = 0;
    // Never reset, so the gap between two measurements gives a rate rather than a total.
    unsigned long long g_totalDraws = 0;
    unsigned long long g_drawsAtLastDump = 0;

    LARGE_INTEGER g_tickFrequency = {};
    LARGE_INTEGER g_lastTick = {};
    float g_sinceCensus = 0.0f;
    float g_sinceDump = 0.0f;

    float PassSeconds() {
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

    void Describe(IDirect3DDevice9* device, Record& out, bool indexed, D3DPRIMITIVETYPE type,
                  UINT vertices, UINT primitives, UINT startIndex, const D3DVIEWPORT9& viewport) {
        out = Record{};
        out.indexed = indexed;
        out.type = type;
        out.vertices = vertices;
        out.primitives = primitives;
        out.startIndex = startIndex;
        out.minZ = viewport.MinZ;
        out.maxZ = viewport.MaxZ;

        IDirect3DVertexBuffer9* stream = nullptr;
        UINT offset = 0;
        if (SUCCEEDED(device->GetStreamSource(0, &stream, &offset, &out.stride)) &&
            stream != nullptr) {
            stream->Release();
        }

        IDirect3DSurface9* target = nullptr;
        if (SUCCEEDED(device->GetRenderTarget(0, &target)) && target != nullptr) {
            D3DSURFACE_DESC desc = {};
            target->GetDesc(&desc);
            out.targetWidth = desc.Width;
            out.targetHeight = desc.Height;
            target->Release();
        }

        device->GetRenderState(D3DRS_ZENABLE, &out.zEnable);
        device->GetRenderState(D3DRS_ZWRITEENABLE, &out.zWrite);
        device->GetRenderState(D3DRS_ZFUNC, &out.zFunc);
        device->GetRenderState(D3DRS_CULLMODE, &out.cull);
        device->GetRenderState(D3DRS_ALPHABLENDENABLE, &out.alphaBlend);
        device->GetRenderState(D3DRS_SRCBLEND, &out.srcBlend);
        device->GetRenderState(D3DRS_DESTBLEND, &out.destBlend);
        device->GetRenderState(D3DRS_ALPHATESTENABLE, &out.alphaTest);

        // How long the bytecode is, rather than which shader object it was: a pointer is a
        // different one every run, and the length is cheap enough to compare on the hot path once
        // the census has said which length the dome has.
        IDirect3DPixelShader9* shader = nullptr;
        if (SUCCEEDED(device->GetPixelShader(&shader)) && shader != nullptr) {
            UINT size = 0;
            if (SUCCEEDED(shader->GetFunction(nullptr, &size))) {
                out.shaderSize = size;
            }
            shader->Release();
        }

        for (DWORD sampler = 0; sampler < kSamplers; sampler++) {
            IDirect3DBaseTexture9* texture = nullptr;
            if (FAILED(device->GetTexture(sampler, &texture)) || texture == nullptr) {
                continue;
            }
            if (texture->GetType() == D3DRTYPE_TEXTURE) {
                D3DSURFACE_DESC desc = {};
                if (SUCCEEDED(static_cast<IDirect3DTexture9*>(texture)->GetLevelDesc(0, &desc))) {
                    out.textureWidth[sampler] = desc.Width;
                    out.textureHeight[sampler] = desc.Height;
                    out.textureFormat[sampler] = desc.Format;
                }
            }
            texture->Release();
        }

        float camera[8] = {};
        if (SUCCEEDED(device->GetVertexShaderConstantF(45, camera, 2))) {
            for (size_t i = 0; i < 3; i++) {
                out.position[i] = camera[i];
                out.direction[i] = camera[4 + i];
            }
        }
    }

    void Observe(IDirect3DDevice9* device, bool indexed, D3DPRIMITIVETYPE type, UINT vertices,
                 UINT primitives, UINT startIndex) {
        g_totalDraws++;
        if (!g_measuring) {
            return;
        }
        g_windowDraws++;

        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport)) ||
            viewport.MinZ < SkyOverhaul::Frame::kSkyPassMinZ) {
            return;
        }
        g_skyDraws++;
        if (g_recorded >= kMaxRecords) {
            return;
        }
        Describe(device, g_records[g_recorded++], indexed, type, vertices, primitives, startIndex,
                 viewport);
    }

    HRESULT __stdcall DrawPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                          UINT startVertex, UINT primitiveCount) {
        Observe(device, false, type, 0, primitiveCount, startVertex);
        return g_originalDrawPrimitive(device, type, startVertex, primitiveCount);
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex,
                                                 UINT numVertices, UINT startIndex,
                                                 UINT primitiveCount) {
        Observe(device, true, type, numVertices, primitiveCount, startIndex);
        return g_originalDrawIndexedPrimitive(device, type, baseVertexIndex, minVertexIndex,
                                              numVertices, startIndex, primitiveCount);
    }

    void Dump(const SkyOverhaul::Frame::Pass& pass) {
        const float rate =
            g_sinceDump > 0.0f
                ? static_cast<float>(g_totalDraws - g_drawsAtLastDump) / g_sinceDump
                : 0.0f;
        SkyOverhaul::Logf("dome f%u: %u draws at the far plane out of %u in the window, %s pass, "
                          "%ux%u, %.0f draws a second overall",
                          pass.frame, g_skyDraws, g_windowDraws, pass.sky ? "sky" : "other",
                          pass.backBuffer.Width, pass.backBuffer.Height, rate);

        for (size_t i = 0; i < g_recorded; i++) {
            const Record& record = g_records[i];
            const unsigned index = static_cast<unsigned>(i);
            SkyOverhaul::Logf("dome f%u #%u: %s %s v=%u p=%u start=%u stride=%u rt=%ux%u ps=%ub",
                              pass.frame, index, record.indexed ? "dip" : "dp",
                              PrimitiveName(record.type), record.vertices, record.primitives,
                              record.startIndex, record.stride, record.targetWidth,
                              record.targetHeight, record.shaderSize);
            SkyOverhaul::Logf("dome f%u #%u: z %.4f-%.4f test=%u write=%u func=%u | blend=%u %u/%u "
                              "| cull=%u atest=%u",
                              pass.frame, index, record.minZ, record.maxZ, record.zEnable,
                              record.zWrite, record.zFunc, record.alphaBlend, record.srcBlend,
                              record.destBlend, record.cull, record.alphaTest);

            char textures[160] = {};
            size_t written = 0;
            for (DWORD sampler = 0; sampler < kSamplers; sampler++) {
                char format[16] = "-";
                if (record.textureWidth[sampler] != 0) {
                    FormatName(record.textureFormat[sampler], format, sizeof(format));
                }
                const int added = std::snprintf(textures + written, sizeof(textures) - written,
                                                " t%u %ux%u %s", static_cast<unsigned>(sampler),
                                                record.textureWidth[sampler],
                                                record.textureHeight[sampler], format);
                if (added <= 0 || written + static_cast<size_t>(added) >= sizeof(textures)) {
                    break;
                }
                written += static_cast<size_t>(added);
            }
            SkyOverhaul::Logf("dome f%u #%u:%s | c45 (%.1f %.1f %.1f) c46 (%.2f %.2f %.2f)",
                              pass.frame, index, textures, record.position[0], record.position[1],
                              record.position[2], record.direction[0], record.direction[1],
                              record.direction[2]);
        }
    }

    void OpenWindow() {
        g_recorded = 0;
        g_windowDraws = 0;
        g_skyDraws = 0;
        g_measuring = true;
    }
}

bool SkyOverhaul::DomeDraw::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    QueryPerformanceFrequency(&g_tickFrequency);

    void* indexed = Vtable::Slot(kDrawIndexedPrimitiveSlot);
    if (indexed == nullptr) {
        api->Log("dome: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }
    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    if (!api->Hook(indexed, reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                   reinterpret_cast<void**>(&g_originalDrawIndexedPrimitive))) {
        return false;
    }

    // The unindexed entry is watched too, in case the dome turns out to be drawn through it, but
    // losing that one only narrows the census rather than ending it.
    void* plain = Vtable::Slot(kDrawPrimitiveSlot);
    if (plain == nullptr || !api->Hook(plain, reinterpret_cast<void*>(&DrawPrimitiveDetour),
                                       reinterpret_cast<void**>(&g_originalDrawPrimitive))) {
        Logf("dome: DrawPrimitive could not be followed, so only indexed draws are measured");
    }

    Logf("dome: measuring one sky pass every %.0f seconds", kCensusSeconds);
    return true;
}

void SkyOverhaul::DomeDraw::OnScenePass(const Frame::Pass& pass) {
    const float elapsed = PassSeconds();
    g_sinceDump += elapsed;

    if (g_measuring) {
        // Whatever has been counted belongs to the pass that is ending here. A pass with nothing
        // at the far end of the depth range is not the sky's, so rather than spend a whole cycle
        // on it the window stays open and the pass after it is measured instead.
        if (g_skyDraws == 0) {
            OpenWindow();
            return;
        }
        Dump(pass);
        g_measuring = false;
        g_sinceCensus = 0.0f;
        g_sinceDump = 0.0f;
        g_drawsAtLastDump = g_totalDraws;
        return;
    }

    // Only with a world submitted: a menu has no sky pass to find, so the window would stay open
    // indefinitely, asking every draw in the frame where it is.
    g_sinceCensus += elapsed;
    if (pass.live && g_sinceCensus >= kCensusSeconds) {
        OpenWindow();
    }
}
