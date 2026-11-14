#include "engine/sky_pass.h"

#include "engine/com.h"
#include "engine/memory.h"

#include <algorithm>

namespace {
    // The engine device's EndScene, __thiscall declared __fastcall.
    using EndSceneFn = HRESULT(__fastcall*)(uint8_t* self);

    // Its whole body, which the address library lacks; the first seven bytes, the ones another
    // plugin's hook overwrites, are wildcards.
    FCSE::Relocation<EndSceneFn> g_endScene{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? ?? 8B 41 38 8B 08 8B 91 A8 00 00 00 50 FF D2 C3")};
    EndSceneFn g_originalEndScene = nullptr;

    // The engine device's Direct3D device.
    constexpr ptrdiff_t kDevice = 0x38;

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

    ThirdPerson::SkyPass::PassFn g_onPass = nullptr;

    // Whether the sky pass has run since the frame's composite. Render thread.
    bool g_passed = false;

    // An offscreen target of the back buffer's size, with depth.
    bool IsWorld(IDirect3DDevice9* device) {
        ThirdPerson::Surface target;
        ThirdPerson::Surface backBuffer;
        ThirdPerson::Surface depth;
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

    // The back buffer with no depth: the composite, which ends the frame's world.
    bool IsComposite(IDirect3DDevice9* device) {
        ThirdPerson::Surface depth;
        if (SUCCEEDED(device->GetDepthStencilSurface(&depth.surface)) && depth.surface != nullptr) {
            return false;
        }
        ThirdPerson::Surface target;
        ThirdPerson::Surface backBuffer;
        return SUCCEEDED(device->GetRenderTarget(0, &target.surface)) &&
               SUCCEEDED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) &&
               target.surface != nullptr && target.surface == backBuffer.surface;
    }

    bool ReadView(IDirect3DDevice9* device, ThirdPerson::SkyPass::View& out) {
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

    HRESULT __fastcall EndSceneDetour(uint8_t* self) {
        IDirect3DDevice9* device = ThirdPerson::Field<IDirect3DDevice9*>(self, kDevice);
        ThirdPerson::SkyPass::Pass pass = {device};
        if (g_passed) {
            g_passed = !IsComposite(device);
        } else if (SUCCEEDED(device->GetViewport(&pass.viewport)) &&
                   pass.viewport.MinZ >= kSkyPassMinZ && device->TestCooperativeLevel() == D3D_OK &&
                   IsWorld(device) && ReadView(device, pass.view)) {
            g_passed = true;
            g_onPass(pass);
        }
        return g_originalEndScene(self);
    }
}

bool ThirdPerson::SkyPass::Install(PassFn onPass) {
    g_onPass = onPass;
    if (!Hook(g_endScene.address(), &EndSceneDetour, &g_originalEndScene)) {
        FCSE::Logf("sky pass: the engine device's EndScene cannot be hooked; no shadow is drawn");
        return false;
    }
    return true;
}
