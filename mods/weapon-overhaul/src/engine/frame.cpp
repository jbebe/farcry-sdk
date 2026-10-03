#include "engine/frame.h"

#include "engine/com.h"
#include "engine/vtable.h"

namespace {
    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9*);

    constexpr float kSkyPassMinZ = 0.9f;

    EndSceneFn g_originalEndScene = nullptr;
    WeaponOverhaul::Frame::PassFn g_onScenePass = nullptr;
    WeaponOverhaul::Frame::CompositeFn g_onComposite = nullptr;

    uint32_t g_passSerial = 0;
    uint32_t g_frame = 0;
    bool g_pastSky = false;
    UINT g_width = 0;
    UINT g_height = 0;

    struct SurfaceRef {
        IDirect3DSurface9* surface = nullptr;
        ~SurfaceRef() { WeaponOverhaul::Release(surface); }
    };

    void Observe(IDirect3DDevice9* device) {
        if (device->TestCooperativeLevel() != D3D_OK) {
            return;
        }
        SurfaceRef target;
        SurfaceRef backBuffer;
        if (FAILED(device->GetRenderTarget(0, &target.surface)) || target.surface == nullptr ||
            FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) ||
            backBuffer.surface == nullptr) {
            return;
        }
        D3DSURFACE_DESC targetDesc = {};
        D3DSURFACE_DESC backBufferDesc = {};
        target.surface->GetDesc(&targetDesc);
        backBuffer.surface->GetDesc(&backBufferDesc);
        g_width = backBufferDesc.Width;
        g_height = backBufferDesc.Height;

        SurfaceRef depth;
        const bool haveDepth =
            SUCCEEDED(device->GetDepthStencilSurface(&depth.surface)) && depth.surface != nullptr;
        const bool toBackBuffer = target.surface == backBuffer.surface;
        if (toBackBuffer && !haveDepth) {
            g_onComposite(device);
            g_pastSky = false;
            g_frame++;
            return;
        }
        if (toBackBuffer || !haveDepth || targetDesc.Width != backBufferDesc.Width ||
            targetDesc.Height != backBufferDesc.Height) {
            return;
        }

        D3DVIEWPORT9 viewport = {};
        if (SUCCEEDED(device->GetViewport(&viewport)) && viewport.MinZ >= kSkyPassMinZ) {
            g_pastSky = true;
        }
        g_onScenePass({device, target.surface, g_passSerial});
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        Observe(device);
        g_passSerial++;
        return g_originalEndScene(device);
    }
}

bool WeaponOverhaul::Frame::Install(PassFn onScenePass, CompositeFn onComposite) {
    g_onScenePass = onScenePass;
    g_onComposite = onComposite;
    return Vtable::Hook(Vtable::kEndScene, reinterpret_cast<void*>(&EndSceneDetour),
                        reinterpret_cast<void**>(&g_originalEndScene));
}

uint32_t WeaponOverhaul::Frame::PassSerial() {
    return g_passSerial;
}

uint32_t WeaponOverhaul::Frame::Number() {
    return g_frame;
}

bool WeaponOverhaul::Frame::PastSky() {
    return g_pastSky;
}

UINT WeaponOverhaul::Frame::Width() {
    return g_width;
}

UINT WeaponOverhaul::Frame::Height() {
    return g_height;
}
