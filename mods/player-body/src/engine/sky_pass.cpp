#include "engine/sky_pass.h"

#include "engine/com.h"
#include "engine/vtable.h"

#include <algorithm>

namespace {
    constexpr size_t kPresentSlot = 17;
    constexpr size_t kEndSceneSlot = 42;

    // The only world pass whose depth range is squeezed against the far plane.
    constexpr float kSkyPassMinZ = 0.9f;

    // Bound for every shader in the frame: the projection's first two rows at c8, the view distance
    // in c40, and c45 to c54, the camera's place and direction, the fog, then its right and up.
    constexpr UINT kProjection = 8;
    constexpr UINT kDistances = 40;
    constexpr UINT kCamera = 45;
    constexpr UINT kCameraRows = 10;
    constexpr size_t kViewDistance = 2;
    constexpr size_t kForward = 4;
    constexpr size_t kRight = 32;
    constexpr size_t kUp = 36;

    using PresentFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, const RECT* source,
                                          const RECT* destination, HWND window, const RGNDATA* dirty);
    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9* device);
    PresentFn g_originalPresent = nullptr;
    EndSceneFn g_originalEndScene = nullptr;

    PlayerBody::SkyPass::PassFn g_onPass = nullptr;

    // Whether a pass has run since the last Present. Render thread.
    bool g_passed = false;

    // An offscreen target of the back buffer's size, with depth.
    bool IsWorld(IDirect3DDevice9* device) {
        PlayerBody::Surface target;
        PlayerBody::Surface backBuffer;
        PlayerBody::Surface depth;
        if (FAILED(device->GetRenderTarget(0, &target.surface)) || target.surface == nullptr ||
            FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) ||
            backBuffer.surface == nullptr || target.surface == backBuffer.surface ||
            FAILED(device->GetDepthStencilSurface(&depth.surface)) || depth.surface == nullptr) {
            return false;
        }
        D3DSURFACE_DESC targetDesc = {};
        D3DSURFACE_DESC backBufferDesc = {};
        target.surface->GetDesc(&targetDesc);
        backBuffer.surface->GetDesc(&backBufferDesc);
        return targetDesc.Width == backBufferDesc.Width && targetDesc.Height == backBufferDesc.Height;
    }

    bool ReadView(IDirect3DDevice9* device, PlayerBody::SkyPass::View& out) {
        float projection[2 * 4];
        float distances[4];
        float camera[kCameraRows * 4];
        if (FAILED(device->GetVertexShaderConstantF(kProjection, projection, 2)) ||
            FAILED(device->GetVertexShaderConstantF(kDistances, distances, 1)) ||
            FAILED(device->GetVertexShaderConstantF(kCamera, camera, kCameraRows)) ||
            projection[0] == 0.0f || projection[5] == 0.0f) {
            return false;
        }
        std::copy_n(camera, 3, out.eye);
        std::copy_n(camera + kForward, 3, out.forward);
        std::copy_n(camera + kRight, 3, out.right);
        std::copy_n(camera + kUp, 3, out.up);
        out.halfWidth = 1.0f / projection[0];
        out.halfHeight = 1.0f / projection[5];
        out.viewDistance = distances[kViewDistance];
        return true;
    }

    HRESULT __stdcall PresentDetour(IDirect3DDevice9* device, const RECT* source,
                                    const RECT* destination, HWND window, const RGNDATA* dirty) {
        g_passed = false;
        return g_originalPresent(device, source, destination, window, dirty);
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        PlayerBody::SkyPass::Pass pass = {device};
        if (!g_passed && SUCCEEDED(device->GetViewport(&pass.viewport)) &&
            pass.viewport.MinZ >= kSkyPassMinZ && device->TestCooperativeLevel() == D3D_OK &&
            IsWorld(device) && ReadView(device, pass.view)) {
            g_passed = true;
            g_onPass(pass);
        }
        return g_originalEndScene(device);
    }
}

bool PlayerBody::SkyPass::Install(PassFn onPass) {
    g_onPass = onPass;
    return Vtable::Hook(kPresentSlot, &PresentDetour, &g_originalPresent) &&
           Vtable::Hook(kEndSceneSlot, &EndSceneDetour, &g_originalEndScene);
}
