#include "engine/frame.h"

#include "engine/com.h"
#include "engine/vtable.h"
#include "fcse_api.h"

namespace {
    constexpr size_t kEndSceneSlot = 42;

    // The sky pass is the only scene pass whose depth range is squeezed against the far plane.
    constexpr float kSkyPassMinZ = 0.9f;

    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9*);

    EndSceneFn g_originalEndScene = nullptr;
    VehicleOverhaul::Frame::PassFn g_onSkyPass = nullptr;
    VehicleOverhaul::Frame::PassFn g_onComposite = nullptr;

    // The first scene pass opens a frame and the composite closes it.
    bool g_inFrame = false;
    bool g_skyDone = false;

    void Observe(IDirect3DDevice9* device) {
        if (device->TestCooperativeLevel() != D3D_OK) {
            return;
        }

        IDirect3DSurface9* target = nullptr;
        IDirect3DSurface9* backBuffer = nullptr;
        IDirect3DSurface9* depth = nullptr;
        device->GetRenderTarget(0, &target);
        device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer);
        const bool haveDepth = SUCCEEDED(device->GetDepthStencilSurface(&depth)) && depth != nullptr;

        D3DSURFACE_DESC targetDesc = {};
        D3DSURFACE_DESC backBufferDesc = {};
        if (target != nullptr && backBuffer != nullptr) {
            target->GetDesc(&targetDesc);
            backBuffer->GetDesc(&backBufferDesc);
        }
        const bool toBackBuffer = target != nullptr && target == backBuffer;
        VehicleOverhaul::Release(target);
        VehicleOverhaul::Release(backBuffer);
        VehicleOverhaul::Release(depth);
        if (backBufferDesc.Width == 0) {
            return;
        }

        // The world renders into an offscreen target of the back buffer's size, and the composite
        // that resolves it is the only back-buffer pass with no depth attached.
        const bool sceneSized = targetDesc.Width == backBufferDesc.Width &&
                                targetDesc.Height == backBufferDesc.Height;
        const bool scene = haveDepth && sceneSized && !toBackBuffer;
        const bool composite = toBackBuffer && !haveDepth;

        VehicleOverhaul::Frame::Pass pass = {device, {}};
        if (FAILED(device->GetViewport(&pass.viewport))) {
            return;
        }

        if (scene) {
            if (!g_inFrame) {
                g_inFrame = true;
                g_skyDone = false;
            }
            if (!g_skyDone && pass.viewport.MinZ >= kSkyPassMinZ) {
                g_skyDone = true;
                g_onSkyPass(pass);
            }
        } else if (composite && g_inFrame) {
            g_inFrame = false;
            g_onComposite(pass);
        }
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        Observe(device);
        return g_originalEndScene(device);
    }
}

bool VehicleOverhaul::Frame::Install(PassFn onSkyPass, PassFn onComposite) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    void* endScene = Vtable::Slot(kEndSceneSlot);
    if (endScene == nullptr) {
        api->Log("frame: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }
    g_onSkyPass = onSkyPass;
    g_onComposite = onComposite;
    return api->Hook(endScene, reinterpret_cast<void*>(&EndSceneDetour),
                     reinterpret_cast<void**>(&g_originalEndScene));
}
