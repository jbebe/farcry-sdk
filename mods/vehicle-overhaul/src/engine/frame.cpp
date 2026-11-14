#include "engine/frame.h"

#include "engine/com.h"
#include "engine/memory.h"
#include "fcse_api.h"

namespace {
    // The engine device's EndScene, __thiscall declared __fastcall.
    using EndSceneFn = HRESULT(__fastcall*)(void* self);

    // Its whole body, which the address library lacks; the first seven bytes, the ones another
    // plugin's hook overwrites, are wildcards.
    FCSE::Relocation<EndSceneFn> g_endScene{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? ?? 8B 41 38 8B 08 8B 91 A8 00 00 00 50 FF D2 C3")};

    // The engine device's Direct3D device.
    constexpr ptrdiff_t kDevice = 0x38;

    // The sky pass is the only scene pass whose depth range is squeezed against the far plane.
    constexpr float kSkyPassMinZ = 0.9f;

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

    HRESULT __fastcall EndSceneDetour(void* self) {
        Observe(VehicleOverhaul::At<IDirect3DDevice9*>(self, kDevice));
        return g_originalEndScene(self);
    }
}

bool VehicleOverhaul::Frame::Install(PassFn onSkyPass, PassFn onComposite) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_endScene) {
        api->Log("frame: the engine device's EndScene was not found in this build");
        return false;
    }
    g_onSkyPass = onSkyPass;
    g_onComposite = onComposite;
    return api->Hook(reinterpret_cast<void*>(g_endScene.address()),
                     reinterpret_cast<void*>(&EndSceneDetour),
                     reinterpret_cast<void**>(&g_originalEndScene));
}
