#include "engine/frame.h"

#include "engine/com.h"
#include "engine/vtable.h"

#include <array>
#include <cstddef>

namespace {
    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9*);

    constexpr float kSkyPassMinZ = 0.9f;

    EndSceneFn g_originalEndScene = nullptr;
    AimingOverhaul::Frame::PassFn g_onScenePass = nullptr;
    // The composite's callbacks, in the order they run.
    std::array<AimingOverhaul::Frame::CompositeFn, 4> g_onComposites = {};
    size_t g_composites = 0;

    uint32_t g_passSerial = 0;
    uint32_t g_frame = 0;
    bool g_pastSky = false;
    UINT g_width = 0;
    UINT g_height = 0;

    struct SurfaceRef {
        IDirect3DSurface9* surface = nullptr;
        ~SurfaceRef() { AimingOverhaul::Release(surface); }
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
            for (size_t i = 0; i < g_composites; ++i) {
                g_onComposites[i](device);
            }
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

bool AimingOverhaul::Frame::Install(PassFn onScenePass, CompositeFn onComposite) {
    g_onScenePass = onScenePass;
    AddComposite(onComposite);
    return Vtable::Hook(Vtable::kEndScene, &EndSceneDetour, &g_originalEndScene);
}

void AimingOverhaul::Frame::AddComposite(CompositeFn onComposite) {
    if (g_composites < g_onComposites.size()) {
        g_onComposites[g_composites++] = onComposite;
    }
}

uint32_t AimingOverhaul::Frame::PassSerial() {
    return g_passSerial;
}

uint32_t AimingOverhaul::Frame::Number() {
    return g_frame;
}

bool AimingOverhaul::Frame::PastSky() {
    return g_pastSky;
}

UINT AimingOverhaul::Frame::Width() {
    return g_width;
}

UINT AimingOverhaul::Frame::Height() {
    return g_height;
}
