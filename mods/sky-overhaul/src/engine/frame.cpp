#include "engine/frame.h"

#include "engine/cloud_layer.h"
#include "engine/engine_device.h"
#include "fcse_api.h"

#include <windows.h>

namespace {
    // __thiscall taking only `this`, declared __fastcall because MSVC will not let a free function
    // be __thiscall.
    using EndSceneFn = HRESULT(__fastcall*)(void* self);

    // Its whole body: it clears the renderer's in-scene flag, the first seven bytes and the ones a
    // hook placed by another plugin first overwrites, then ends the device's scene. The address
    // library does not carry it.
    FCSE::Relocation<EndSceneFn> g_endScene{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? ?? 8B 41 38 8B 08 8B 91 A8 00 00 00 50 FF D2 C3")};

    EndSceneFn g_originalEndScene = nullptr;
    SkyOverhaul::Frame::PassFn g_onScenePass = nullptr;
    SkyOverhaul::Frame::PassFn g_onFinalPass = nullptr;
    SkyOverhaul::Frame::PassFn g_onOffscreenPass = nullptr;

    // The first scene pass opens a frame and the composite closes it. Every scene pass between the
    // two belongs to the frame already open.
    enum class State { AwaitingScene, InFrame };
    State g_state = State::AwaitingScene;

    uint32_t g_frame = 0;
    uint32_t g_skyFrame = 0;
    uint32_t g_passSerial = 0;
    uint32_t g_lastSubmitCount = 0;
    bool g_live = false;

    struct SurfaceRef {
        IDirect3DSurface9* surface = nullptr;

        ~SurfaceRef() {
            if (surface != nullptr) {
                surface->Release();
            }
        }
    };

    void Observe(IDirect3DDevice9* device) {
        if (device->TestCooperativeLevel() != D3D_OK) {
            return;
        }

        SurfaceRef target;
        if (FAILED(device->GetRenderTarget(0, &target.surface)) || target.surface == nullptr) {
            return;
        }
        D3DSURFACE_DESC targetDesc = {};
        target.surface->GetDesc(&targetDesc);

        SurfaceRef backBuffer;
        if (FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) ||
            backBuffer.surface == nullptr) {
            return;
        }
        D3DSURFACE_DESC backBufferDesc = {};
        backBuffer.surface->GetDesc(&backBufferDesc);

        SurfaceRef depth;
        const bool haveDepth = SUCCEEDED(device->GetDepthStencilSurface(&depth.surface)) &&
                               depth.surface != nullptr;

        const bool sceneSized = targetDesc.Width == backBufferDesc.Width &&
                                targetDesc.Height == backBufferDesc.Height;
        const bool toBackBuffer = target.surface == backBuffer.surface;

        // The world renders into an offscreen target of the back buffer's size, and the composite
        // that resolves it is the only back-buffer pass with no depth attached - the user
        // interface, which follows it, brings a depth surface of its own.
        const bool scene = haveDepth && sceneSized && !toBackBuffer;
        const bool composite = toBackBuffer && !haveDepth;

        bool runScene = false;
        bool runFinal = false;
        const bool runOffscreen = !sceneSized && !toBackBuffer;
        if (scene) {
            if (g_state == State::AwaitingScene) {
                const uint32_t submitCount = SkyOverhaul::CloudLayer::SubmitCount();
                g_live = submitCount != g_lastSubmitCount;
                g_lastSubmitCount = submitCount;
                g_frame++;
                g_state = State::InFrame;
            }
            runScene = true;
        } else if (composite && g_state == State::InFrame) {
            runFinal = true;
            g_state = State::AwaitingScene;
        }

        if (!runScene && !runFinal && !runOffscreen) {
            return;
        }

        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport))) {
            return;
        }

        SkyOverhaul::Frame::Pass pass;
        pass.device = device;
        pass.target = target.surface;
        pass.depth = depth.surface;
        pass.backBuffer = backBufferDesc;
        pass.viewport = viewport;
        // The shadow maps are drawn before the first scene pass opens the frame they belong to.
        pass.frame = runOffscreen && g_state == State::AwaitingScene ? g_frame + 1 : g_frame;
        pass.live = g_live;
        pass.sky = runScene && viewport.MinZ >= SkyOverhaul::Frame::kSkyPassMinZ &&
                   g_skyFrame != g_frame;
        if (pass.sky) {
            g_skyFrame = g_frame;
        }

        if (runScene && g_onScenePass != nullptr) {
            g_onScenePass(pass);
        }
        if (runFinal && g_onFinalPass != nullptr) {
            g_onFinalPass(pass);
        }
        if (runOffscreen && g_onOffscreenPass != nullptr) {
            g_onOffscreenPass(pass);
        }
    }

    HRESULT __fastcall EndSceneDetour(void* self) {
        Observe(SkyOverhaul::EngineDevice::Direct3D(self));
        // Every pass, not only the ones anyone acts on: the draws this counts off are inside all
        // of them.
        g_passSerial++;
        return g_originalEndScene(self);
    }

}

bool SkyOverhaul::Frame::Install(PassFn onScenePass, PassFn onFinalPass, PassFn onOffscreenPass) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_endScene) {
        api->Log("frame: the engine's EndScene was not found in this build");
        return false;
    }

    // A rejected hook is already logged by FCSE.
    if (!api->Hook(reinterpret_cast<void*>(g_endScene.address()),
                   reinterpret_cast<void*>(&EndSceneDetour),
                   reinterpret_cast<void**>(&g_originalEndScene))) {
        return false;
    }

    g_onScenePass = onScenePass;
    g_onFinalPass = onFinalPass;
    g_onOffscreenPass = onOffscreenPass;
    return true;
}

uint32_t SkyOverhaul::Frame::PassSerial() {
    return g_passSerial;
}
