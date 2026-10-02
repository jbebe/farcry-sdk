#include "engine/trace.h"

#include "blur.h"
#include "engine/aim.h"
#include "engine/com.h"
#include "engine/frame.h"
#include "engine/vtable.h"
#include "engine/weapon_draws.h"
#include "fcse_api.h"

#include <windows.h>

namespace {
    using DrawPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, UINT, UINT);

    constexpr size_t kDrawPrimitive = 81;
    constexpr float kSqueezed = 0.5f;
    constexpr DWORD kEveryMilliseconds = 5000;

    DrawPrimitiveFn g_originalDrawPrimitive = nullptr;
    bool g_recording = false;
    DWORD g_lastRecord = 0;
    uint32_t g_frames = 0;

    struct Counts {
        unsigned indexed;
        unsigned plain;
        unsigned squeezed;
        unsigned squeezedPlain;
        unsigned zwrite;
        unsigned colour;
        unsigned stencil;
        unsigned blend;
        unsigned ztest;
        unsigned gunParts;
        D3DVIEWPORT9 firstSqueezed;
    };
    Counts g_counts = {};

    void Count(IDirect3DDevice9* device, bool indexed) {
        indexed ? g_counts.indexed++ : g_counts.plain++;
        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport)) || viewport.MaxZ >= kSqueezed) {
            return;
        }
        if (g_counts.squeezed + g_counts.squeezedPlain == 0) {
            g_counts.firstSqueezed = viewport;
        }
        indexed ? g_counts.squeezed++ : g_counts.squeezedPlain++;
        DWORD state = 0;
        device->GetRenderState(D3DRS_ZWRITEENABLE, &state);
        g_counts.zwrite += state != FALSE;
        device->GetRenderState(D3DRS_COLORWRITEENABLE, &state);
        g_counts.colour += state != 0;
        device->GetRenderState(D3DRS_STENCILENABLE, &state);
        g_counts.stencil += state != FALSE;
        device->GetRenderState(D3DRS_ALPHABLENDENABLE, &state);
        g_counts.blend += state != FALSE;
        device->GetRenderState(D3DRS_ZENABLE, &state);
        g_counts.ztest += state != FALSE;
    }

    HRESULT __stdcall DrawPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                          UINT startVertex, UINT primitiveCount) {
        if (g_recording) {
            Count(device, false);
        }
        return g_originalDrawPrimitive(device, type, startVertex, primitiveCount);
    }
}

bool WeaponOverhaul::Trace::Install() {
    void* draw = Vtable::Slot(kDrawPrimitive);
    return draw != nullptr &&
           FCSE::ApiPointer()->Hook(draw, reinterpret_cast<void*>(&DrawPrimitiveDetour),
                                    reinterpret_cast<void**>(&g_originalDrawPrimitive));
}

void WeaponOverhaul::Trace::Pass(IDirect3DDevice9* device, bool composite, bool pastSky) {
    if (g_recording) {
        IDirect3DSurface9* target = nullptr;
        IDirect3DSurface9* depth = nullptr;
        D3DSURFACE_DESC targetDesc = {};
        D3DSURFACE_DESC depthDesc = {};
        if (SUCCEEDED(device->GetRenderTarget(0, &target)) && target != nullptr) {
            target->GetDesc(&targetDesc);
        }
        if (SUCCEEDED(device->GetDepthStencilSurface(&depth)) && depth != nullptr) {
            depth->GetDesc(&depthDesc);
        }
        D3DVIEWPORT9 viewport = {};
        device->GetViewport(&viewport);
        const D3DVIEWPORT9& s = g_counts.firstSqueezed;
        FCSE::Logf("trace pass %u: target %ux%u fmt %d ms %d, depth %ux%u ms %d, viewport z %.4f-%.4f, "
                   "past sky %d | draws %u indexed %u plain | squeezed %u indexed %u plain "
                   "(z %.4f-%.4f, ztest %u zwrite %u colour %u stencil %u blend %u) | gun parts %u",
                   Frame::PassSerial(), targetDesc.Width, targetDesc.Height, targetDesc.Format,
                   targetDesc.MultiSampleType, depthDesc.Width, depthDesc.Height,
                   depth != nullptr ? static_cast<int>(depthDesc.MultiSampleType) : -1,
                   viewport.MinZ, viewport.MaxZ, pastSky, g_counts.indexed, g_counts.plain,
                   g_counts.squeezed, g_counts.squeezedPlain, s.MinZ, s.MaxZ, g_counts.ztest,
                   g_counts.zwrite, g_counts.colour, g_counts.stencil, g_counts.blend,
                   g_counts.gunParts);
        Release(target);
        Release(depth);
        g_counts = {};
    }
    if (!composite) {
        return;
    }
    if (g_recording) {
        g_recording = false;
        WeaponDraws::Depth depth = {};
        const bool found = WeaponDraws::Latest(depth);
        float metres[2] = {};
        const bool focused = Blur::ReadFocus(device, metres);
        FCSE::Logf("trace frame %u ends: colour pass %u, gun depth %s, vertical scale %.3f, "
                   "depth offset %.4f, focus %s %.3f m, nearest under the aim point %.3f m",
                   g_frames, WeaponDraws::ColourPass(), found ? "drawn" : "missing",
                   depth.verticalScale, depth.depthOffset, focused ? "read" : "unread", metres[0],
                   metres[1]);
        return;
    }
    const DWORD now = GetTickCount();
    if (Aim::Settled() >= 1.0f && now - g_lastRecord >= kEveryMilliseconds) {
        g_lastRecord = now;
        g_recording = true;
        g_frames++;
        g_counts = {};
        FCSE::Logf("trace frame %u begins, settled in the sights", g_frames);
    }
}

void WeaponOverhaul::Trace::IndexedDraw(IDirect3DDevice9* device) {
    if (g_recording) {
        Count(device, true);
    }
}

void WeaponOverhaul::Trace::GunPart() {
    if (g_recording) {
        g_counts.gunParts++;
    }
}
