// Every IDirect3DDevice9 in a process shares one vtable, the one inside d3d9.dll, so a device of
// our own is enough to name the functions the game's device will call - and once those are
// detoured, the device that arrives is the game's. It is released as soon as its vtable has been
// read; nothing of ours stays resident until the game's own frames start arriving.
//
// Reset is hooked before Present, and a failure to hook either leaves neither installed. Drawing
// with Reset unhooked would be the worst of both: ImGui's buffers live in the default pool, so a
// device reset the overlay never hears about fails outright and takes the game's renderer with it.
#include "engine/renderer.h"

#include "fcse_api.h"

#include <cstdio>
#include <d3d9.h>
#include <windows.h>

namespace {
    // IDirect3DDevice9's vtable, as declared in d3d9.h.
    constexpr size_t kResetSlot = 16;
    constexpr size_t kPresentSlot = 17;

    using PresentFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, const RECT* source,
                                          const RECT* destination, HWND window,
                                          const RGNDATA* dirty);
    using ResetFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* params);

    PresentFn g_originalPresent = nullptr;
    ResetFn g_originalReset = nullptr;

    DevTools::Renderer::DrawFn g_draw = nullptr;
    DevTools::Renderer::DeviceLostFn g_onDeviceLost = nullptr;
    bool g_inDraw = false;

    // Draws into the back buffer, whatever the game left bound, and puts its target back. Anything
    // that cannot be undone is not started: without the previous target there is nothing to restore
    // to, so the frame is left alone.
    void DrawToBackBuffer(IDirect3DDevice9* device) {
        IDirect3DSurface9* previousTarget = nullptr;
        if (FAILED(device->GetRenderTarget(0, &previousTarget)) || previousTarget == nullptr) {
            return;
        }

        IDirect3DSurface9* backBuffer = nullptr;
        if (SUCCEEDED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer))) {
            if (SUCCEEDED(device->SetRenderTarget(0, backBuffer))) {
                // Present is called outside the game's scene, and drawing needs one of our own.
                if (SUCCEEDED(device->BeginScene())) {
                    g_draw(device);
                    device->EndScene();
                }
                device->SetRenderTarget(0, previousTarget);
            }
            backBuffer->Release();
        }

        previousTarget->Release();
    }

    HRESULT __stdcall PresentDetour(IDirect3DDevice9* device, const RECT* source,
                                    const RECT* destination, HWND window, const RGNDATA* dirty) {
        // A draw that presented a frame of its own would otherwise recurse for as long as it kept
        // drawing.
        if (!g_inDraw) {
            g_inDraw = true;
            DrawToBackBuffer(device);
            g_inDraw = false;
        }
        return g_originalPresent(device, source, destination, window, dirty);
    }

    HRESULT __stdcall ResetDetour(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* params) {
        g_onDeviceLost();
        return g_originalReset(device, params);
    }

    // Builds a device only to read its vtable. The device is gone before this returns.
    bool ReadDeviceVtable(void** outPresent, void** outReset) {
        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return false;
        }

        D3DPRESENT_PARAMETERS present{};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();

        IDirect3DDevice9* device = nullptr;
        HRESULT created = d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                            D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device);
        if (FAILED(created)) {
            d3d->Release();
            return false;
        }

        void** vtable = *reinterpret_cast<void***>(device);
        *outPresent = vtable[kPresentSlot];
        *outReset = vtable[kResetSlot];

        device->Release();
        d3d->Release();
        return true;
    }
}

namespace DevTools::Renderer {

bool Install(DrawFn draw, DeviceLostFn onDeviceLost) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    g_draw = draw;
    g_onDeviceLost = onDeviceLost;

    void* present = nullptr;
    void* reset = nullptr;
    if (!ReadDeviceVtable(&present, &reset)) {
        api->Log("renderer: no Direct3D 9 device could be created - the overlay cannot draw");
        return false;
    }

    // A rejected hook is already logged by FCSE, naming the plugin that owns the address. Reset
    // first: Present is what starts drawing, and drawing without Reset is what breaks the game.
    if (!api->Hook(reset, reinterpret_cast<void*>(&ResetDetour),
                   reinterpret_cast<void**>(&g_originalReset)) ||
        !api->Hook(present, reinterpret_cast<void*>(&PresentDetour),
                   reinterpret_cast<void**>(&g_originalPresent))) {
        return false;
    }

    char line[128];
    std::snprintf(line, sizeof(line), "renderer: drawing from Present at 0x%08zX",
                  reinterpret_cast<size_t>(present));
    api->Log(line);
    return true;
}

}
