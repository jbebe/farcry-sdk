#include "engine/solid_depth.h"

#include "engine/com.h"
#include "engine/known_shaders.h"
#include "engine/render_target.h"
#include "fcse_api.h"

namespace {
    // A render target that takes no memory and a depth format a shader can read, both driver
    // formats rather than Direct3D's own.
    constexpr D3DFORMAT kNullFormat = static_cast<D3DFORMAT>(MAKEFOURCC('N', 'U', 'L', 'L'));
    constexpr D3DFORMAT kReadableDepth = static_cast<D3DFORMAT>(MAKEFOURCC('I', 'N', 'T', 'Z'));

    // A viewport squeezed this close to the near plane is the first-person weapon's depth pass.
    constexpr float kWeaponFarthest = 0.5f;

    bool g_enabled = false;
    // From the composite to the sky pass, which is when the world's depth pass draws.
    bool g_gathering = false;
    // Whether the frame has drawn into the depth yet, which the first draw clears it for.
    bool g_drawn = false;
    bool g_ready = false;
    // The world's depth-stencil surface, borrowed: the engine keeps it alive.
    IDirect3DSurface9* g_sceneDepth = nullptr;

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DSurface9* g_target = nullptr;
    IDirect3DTexture9* g_depthTexture = nullptr;
    IDirect3DSurface9* g_depthSurface = nullptr;
    UINT g_width = 0;
    UINT g_height = 0;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;

    IDirect3DSurface9* g_savedTarget = nullptr;
    D3DVIEWPORT9 g_savedViewport = {};
    RECT g_savedScissor = {};

    void ReleaseTargets() {
        SkyOverhaul::Release(g_target);
        SkyOverhaul::Release(g_depthSurface);
        SkyOverhaul::Release(g_depthTexture);
        g_ready = false;
    }

    bool Refuse(const char* what, HRESULT result) {
        ReleaseTargets();
        g_refused = true;
        FCSE::Logf("solid depth: the device refused the %s, 0x%08lX", what,
                   static_cast<unsigned long>(result));
        return false;
    }

    // The depth and its target at half the scene's size, made on first use.
    bool EnsureTargets(IDirect3DDevice9* device, const D3DSURFACE_DESC& scene) {
        const UINT width = (scene.Width + 1) / 2;
        const UINT height = (scene.Height + 1) / 2;
        if (g_owner == device && g_target != nullptr && g_width == width && g_height == height) {
            return true;
        }
        ReleaseTargets();
        if (g_refused) {
            return false;
        }
        g_owner = device;
        D3DSURFACE_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.Format = kReadableDepth;
        HRESULT created = SkyOverhaul::CreateTarget(device, desc, &g_depthTexture, &g_depthSurface,
                                                    D3DUSAGE_DEPTHSTENCIL);
        if (FAILED(created)) {
            return Refuse("INTZ depth texture", created);
        }
        if (FAILED(created = device->CreateRenderTarget(width, height, kNullFormat,
                                                        D3DMULTISAMPLE_NONE, 0, FALSE, &g_target,
                                                        nullptr))) {
            return Refuse("NULL render target", created);
        }
        g_width = width;
        g_height = height;
        FCSE::Logf("solid depth: %ux%u INTZ depth with a NULL target", width, height);
        return true;
    }

    void Restore(IDirect3DDevice9* device) {
        device->SetRenderTarget(0, g_savedTarget);
        device->SetDepthStencilSurface(g_sceneDepth);
        device->SetViewport(&g_savedViewport);
        device->SetScissorRect(&g_savedScissor);
        SkyOverhaul::Release(g_savedTarget);
    }
}

bool SkyOverhaul::SolidDepth::Begin(IDirect3DDevice9* device) {
    DWORD state = 0;
    if (!g_gathering || FAILED(device->GetRenderState(D3DRS_ZWRITEENABLE, &state)) ||
        state == FALSE) {
        return false;
    }
    D3DVIEWPORT9 viewport = {};
    if (FAILED(device->GetViewport(&viewport)) || viewport.MaxZ < kWeaponFarthest) {
        return false;
    }
    IDirect3DSurface9* depth = nullptr;
    device->GetDepthStencilSurface(&depth);
    const bool scene = depth == g_sceneDepth;
    Release(depth);
    if (!scene || FAILED(device->GetRenderState(D3DRS_STENCILENABLE, &state)) || state != FALSE ||
        KnownShaders::IsFoliageBound(device)) {
        return false;
    }

    g_savedViewport = viewport;
    device->GetScissorRect(&g_savedScissor);
    device->GetRenderTarget(0, &g_savedTarget);
    HRESULT bound = device->SetRenderTarget(0, g_target);
    if (FAILED(bound) || FAILED(bound = device->SetDepthStencilSurface(g_depthSurface))) {
        Restore(device);
        g_gathering = false;
        return Refuse("NULL target and INTZ depth together", bound);
    }
    if (!g_drawn) {
        device->Clear(0, nullptr, D3DCLEAR_ZBUFFER | D3DCLEAR_STENCIL, 0, 1.0f, 0);
        g_drawn = true;
    }
    const D3DVIEWPORT9 half = {viewport.X / 2, viewport.Y / 2, (viewport.Width + 1) / 2,
                               (viewport.Height + 1) / 2, viewport.MinZ, viewport.MaxZ};
    device->SetViewport(&half);
    return true;
}

void SkyOverhaul::SolidDepth::End(IDirect3DDevice9* device) {
    Restore(device);
}

void SkyOverhaul::SolidDepth::OnScenePass(const Frame::Pass& pass) {
    if (!pass.sky) {
        return;
    }
    g_ready = g_gathering && g_drawn;
    g_gathering = false;
    g_drawn = false;
    g_sceneDepth = pass.depth;
    if (g_enabled) {
        EnsureTargets(pass.device, pass.backBuffer);
    }
}

void SkyOverhaul::SolidDepth::OnFinalPass(const Frame::Pass& pass) {
    g_gathering = g_enabled && pass.live && g_depthSurface != nullptr;
}

bool SkyOverhaul::SolidDepth::Latest(Found& out) {
    if (!g_ready) {
        return false;
    }
    out.texture = g_depthTexture;
    out.width = g_width;
    out.height = g_height;
    return true;
}

void SkyOverhaul::SolidDepth::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_sceneDepth = nullptr;
    g_gathering = false;
    g_owner = nullptr;
    g_refused = false;
}

void SkyOverhaul::SolidDepth::SetEnabled(bool enabled) {
    g_enabled = enabled;
    g_gathering = g_gathering && enabled;
}
