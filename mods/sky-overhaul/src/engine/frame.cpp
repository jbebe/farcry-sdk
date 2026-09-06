#include "engine/frame.h"

#include "engine/sky_state.h"
#include "fcse_api.h"

#include <cstdio>
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
    uint32_t g_liveFrame = 0;
    uint32_t g_scenePass = 0;
    uint32_t g_lastSubmitCount = 0;
    bool g_live = false;

    // What the plugin would have to rebuild anything it holds on the device against. The back
    // buffer's surface is replaced by a reset even when its size does not change, so its identity
    // is what says a reset happened; the device's identity says the whole device was rebuilt.
    IDirect3DDevice9* g_device = nullptr;
    IDirect3DSurface9* g_backBufferSurface = nullptr;
    D3DSURFACE_DESC g_backBufferDesc = {};
    int g_deviceLinesLogged = 0;
    int g_lostLinesLogged = 0;

    struct SurfaceRef {
        IDirect3DSurface9* surface = nullptr;

        ~SurfaceRef() {
            if (surface != nullptr) {
                surface->Release();
            }
        }
    };

    void WatchDevice(IDirect3DDevice9* device, IDirect3DSurface9* surface,
                     const D3DSURFACE_DESC& desc) {
        if (device == g_device && surface == g_backBufferSurface &&
            desc.Width == g_backBufferDesc.Width && desc.Height == g_backBufferDesc.Height &&
            desc.Format == g_backBufferDesc.Format &&
            desc.MultiSampleType == g_backBufferDesc.MultiSampleType) {
            return;
        }

        if (g_deviceLinesLogged < 20) {
            g_deviceLinesLogged++;
            char line[224];
            std::snprintf(line, sizeof(line),
                          "device: frame %u, device %p (was %p), back buffer %p (was %p) "
                          "%ux%u fmt %u ms %u",
                          g_frame, static_cast<void*>(device), static_cast<void*>(g_device),
                          static_cast<void*>(surface), static_cast<void*>(g_backBufferSurface),
                          desc.Width, desc.Height, static_cast<unsigned>(desc.Format),
                          static_cast<unsigned>(desc.MultiSampleType));
            FCSE::ApiPointer()->Log(line);
        }

        g_device = device;
        g_backBufferSurface = surface;
        g_backBufferDesc = desc;
    }

    void Observe(IDirect3DDevice9* device) {
        const HRESULT cooperative = device->TestCooperativeLevel();
        if (cooperative != D3D_OK) {
            if (g_lostLinesLogged < 10) {
                g_lostLinesLogged++;
                char line[128];
                std::snprintf(line, sizeof(line), "device: not usable, TestCooperativeLevel 0x%08lX",
                              static_cast<unsigned long>(cooperative));
                FCSE::ApiPointer()->Log(line);
            }
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
        WatchDevice(device, backBuffer.surface, backBufferDesc);

        SurfaceRef depth;
        const bool haveDepth = SUCCEEDED(device->GetDepthStencilSurface(&depth.surface)) &&
                               depth.surface != nullptr;

        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport))) {
            return;
        }

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
                const uint32_t submitCount = SkyOverhaul::SkyState::SubmitCount();
                g_live = submitCount != g_lastSubmitCount;
                g_lastSubmitCount = submitCount;
                g_frame++;
                if (g_live) {
                    g_liveFrame++;
                }
                g_scenePass = 0;
                g_state = State::InFrame;
            }
            g_scenePass++;
            runScene = true;
        } else if (composite && g_state == State::InFrame) {
            runFinal = true;
            g_state = State::AwaitingScene;
        }

        if (!runScene && !runFinal) {
            return;
        }

        SkyOverhaul::Frame::Pass pass;
        pass.device = device;
        pass.backBuffer = backBufferDesc;
        pass.viewport = viewport;
        pass.frame = g_frame;
        pass.liveFrame = g_liveFrame;
        pass.scenePass = g_scenePass;
        pass.live = g_live;

        if (runScene && g_onScenePass != nullptr) {
            g_onScenePass(pass);
        }
        if (runFinal && g_onFinalPass != nullptr) {
            g_onFinalPass(pass);
        }
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        Observe(device);
        return g_originalEndScene(device);
    }

    // Every IDirect3DDevice9 in a process shares one vtable, so a device of our own is enough to
    // name the function the game's device will call. The slot is read while that device is still
    // alive: a wrapper can put its vtable in the object's own allocation.
    void* ReadEndSceneSlot() {
        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return nullptr;
        }

        D3DPRESENT_PARAMETERS present = {};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();

        IDirect3DDevice9* device = nullptr;
        void* endScene = nullptr;
        if (SUCCEEDED(d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device)) &&
            device != nullptr) {
            endScene = (*reinterpret_cast<void***>(device))[kEndSceneSlot];
            device->Release();
        }
        d3d->Release();
        return endScene;
    }
}

bool SkyOverhaul::Frame::Install(PassFn onScenePass, PassFn onFinalPass) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* endScene = ReadEndSceneSlot();
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

    char line[128];
    std::snprintf(line, sizeof(line), "frame: following EndScene at 0x%08zX",
                  reinterpret_cast<size_t>(endScene));
    api->Log(line);
    return true;
}
