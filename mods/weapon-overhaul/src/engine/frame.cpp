#include "engine/frame.h"

#include "engine/com.h"
#include "engine/vtable.h"
#include "fcse_api.h"

namespace {
    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9*);

    // The sky pass is the only world pass whose depth range is squeezed against the far plane.
    constexpr float kSkyPassMinZ = 0.9f;

    EndSceneFn g_originalEndScene = nullptr;
    WeaponOverhaul::Frame::PassFn g_onScenePass = nullptr;

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

        // The composite is the only back-buffer pass with no depth attached; the interface after it
        // brings a depth surface of its own.
        if (toBackBuffer && !haveDepth) {
            g_pastSky = false;
            g_frame++;
            return;
        }
        // The world renders into an offscreen target of the back buffer's size, with depth.
        if (toBackBuffer || !haveDepth || targetDesc.Width != backBufferDesc.Width ||
            targetDesc.Height != backBufferDesc.Height) {
            return;
        }

        D3DVIEWPORT9 viewport = {};
        if (SUCCEEDED(device->GetViewport(&viewport)) && viewport.MinZ >= kSkyPassMinZ) {
            g_pastSky = true;
        }
        g_onScenePass({device, target.surface, depth.surface, backBufferDesc, g_passSerial});
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        Observe(device);
        g_passSerial++;
        return g_originalEndScene(device);
    }
}

bool WeaponOverhaul::Frame::Install(PassFn onScenePass) {
    void* endScene = Vtable::Slot(Vtable::kEndScene);
    if (endScene == nullptr) {
        FCSE::ApiPointer()->Log("frame: no Direct3D 9 device could be made to read from");
        return false;
    }
    g_onScenePass = onScenePass;
    // A rejected hook is already logged by FCSE.
    return FCSE::ApiPointer()->Hook(endScene, reinterpret_cast<void*>(&EndSceneDetour),
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
