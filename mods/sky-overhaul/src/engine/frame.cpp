#include "engine/frame.h"

#include "engine/cloud_layer.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <windows.h>

namespace {
    constexpr size_t kEndSceneSlot = 42;

    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9*);

    EndSceneFn g_originalEndScene = nullptr;
    SkyOverhaul::Frame::PassFn g_onScenePass = nullptr;
    SkyOverhaul::Frame::PassFn g_onFinalPass = nullptr;

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

        if (!runScene && !runFinal) {
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
        pass.frame = g_frame;
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
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        Observe(device);
        // Every pass, not only the ones anyone acts on: the draws this counts off are inside all
        // of them.
        g_passSerial++;
        return g_originalEndScene(device);
    }

}

bool SkyOverhaul::Frame::Install(PassFn onScenePass, PassFn onFinalPass) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* endScene = Vtable::Slot(kEndSceneSlot);
    if (endScene == nullptr) {
        api->Log("frame: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }

    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    if (!api->Hook(endScene, reinterpret_cast<void*>(&EndSceneDetour),
                   reinterpret_cast<void**>(&g_originalEndScene))) {
        return false;
    }

    g_onScenePass = onScenePass;
    g_onFinalPass = onFinalPass;
    return true;
}

uint32_t SkyOverhaul::Frame::PassSerial() {
    return g_passSerial;
}
