// The overlay draws just before the engine's Present calls Direct3D's, the frame is timed just
// after, and the engine's Reset frees what the overlay holds. Reset is hooked first and the draw
// last: ImGui's buffers live in the default pool, so a reset the overlay missed would fail.
#include "engine/renderer.h"

#include "engine/frame_capture.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <d3d9.h>

namespace {
    // The engine device's Reset, __thiscall declared __fastcall; true when Direct3D's reset
    // succeeded.
    using ResetFn = bool(__fastcall*)(void* self);
    FCSE::Relocation<ResetFn> g_reset{FCSE::Uplay(0x00422630)};
    ResetFn g_originalReset = nullptr;

    // Direct3D's Present inside the engine's Present, which the address library lacks, between the
    // calls that let another thread borrow the device and take it back; ESI is the engine device.
    FCSE::Relocation<uint8_t*> g_presentCall{FCSE::Pattern(
        "BD 01 00 00 00 55 6A 02 E8 ?? ?? ?? ?? 8B 54 24 20 8B 46 38 8B 08 83 C4 08 53 52 "
        "8B 54 24 24 52 8B 54 24 2C 52 50 8B 41 44 FF D0 6A 02 55 8B F8 E8 ?? ?? ?? ?? "
        "B8 FF FF FF 7F")};
    // Before the device is lent, and once it is taken back: the render thread owns it at both.
    constexpr ptrdiff_t kBeforePresent = 0x00;
    constexpr ptrdiff_t kAfterPresent = 0x35;

    // The engine device's Direct3D device.
    constexpr ptrdiff_t kDevice = 0x38;

    constexpr size_t kMaxSubscribers = 4;
    DevTools::Renderer::FrameFn g_subscribers[kMaxSubscribers]{};
    size_t g_subscriberCount = 0;

    DevTools::Renderer::DrawFn g_draw = nullptr;
    DevTools::Renderer::DeviceLostFn g_onDeviceLost = nullptr;

    IDirect3DDevice9* DeviceOf(const FCSE_MidHookContext* ctx) {
        return DevTools::Read<IDirect3DDevice9*>(reinterpret_cast<const void*>(ctx->esi), kDevice);
    }

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

    void BeforePresent(FCSE_MidHookContext* ctx) {
        IDirect3DDevice9* device = DeviceOf(ctx);
        const DevTools::FrameStats::Frame frame = DevTools::FrameStats::Presenting();
        for (size_t i = 0; i < g_subscriberCount; ++i) {
            g_subscribers[i](frame);
        }
        DevTools::FrameCapture::OnPresent(device);
        DrawToBackBuffer(device);
    }

    void AfterPresent(FCSE_MidHookContext* ctx) {
        DevTools::FrameStats::Presented(DeviceOf(ctx));
    }

    bool __fastcall ResetDetour(void* self) {
        DevTools::FrameStats::DeviceLost();
        g_onDeviceLost();
        return g_originalReset(self);
    }
}

namespace DevTools::Renderer {

void Subscribe(FrameFn fn) {
    if (g_subscriberCount < kMaxSubscribers) {
        g_subscribers[g_subscriberCount++] = fn;
    }
}

bool Install(DrawFn draw, DeviceLostFn onDeviceLost) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    g_draw = draw;
    g_onDeviceLost = onDeviceLost;

    if (!g_reset || !g_presentCall) {
        api->Log("renderer: the engine's Reset or Present was not found in this build - the overlay "
                 "cannot draw");
        return false;
    }

    uint8_t* call = g_presentCall.get();
    // A rejected hook is already logged by FCSE.
    if (!api->Hook(reinterpret_cast<void*>(g_reset.address()),
                   reinterpret_cast<void*>(&ResetDetour),
                   reinterpret_cast<void**>(&g_originalReset)) ||
        !api->MidHook(call + kAfterPresent, &AfterPresent) ||
        !api->MidHook(call + kBeforePresent, &BeforePresent)) {
        return false;
    }

    FCSE::Logf("renderer: drawing before the engine's Present at 0x%08zX",
               reinterpret_cast<size_t>(call + kBeforePresent));
    return true;
}

}
