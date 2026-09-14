#include "engine/depth_texture.h"

#include "engine/com.h"
#include "fcse_api.h"

namespace {
    // UncompressDepthWeightsWS, in every pixel shader that reads depth.
    constexpr UINT kMetreWeights = 57;

    // Changes logged before the log goes quiet about them, in case the texture is not one texture.
    constexpr unsigned kLoggedChanges = 4;

    IDirect3DTexture9* g_texture = nullptr;
    float g_metreWeights[4] = {};
    unsigned g_changes = 0;

    // Whether `texture` is a depth the size of the target being drawn into, which a reflection's
    // smaller view is not.
    bool IsSceneDepth(IDirect3DDevice9* device, IDirect3DTexture9* texture) {
        D3DSURFACE_DESC desc = {};
        D3DSURFACE_DESC target = {};
        IDirect3DSurface9* surface = nullptr;
        const bool read = SUCCEEDED(texture->GetLevelDesc(0, &desc)) &&
                          SUCCEEDED(device->GetRenderTarget(0, &surface)) && surface != nullptr &&
                          SUCCEEDED(surface->GetDesc(&target));
        SkyOverhaul::Release(surface);
        return read && desc.Format == D3DFMT_A8R8G8B8 && (desc.Usage & D3DUSAGE_RENDERTARGET) != 0 &&
               desc.Width == target.Width && desc.Height == target.Height;
    }
}

void SkyOverhaul::DepthTexture::Observe(IDirect3DDevice9* device) {
    IDirect3DBaseTexture9* bound = nullptr;
    if (FAILED(device->GetTexture(0, &bound)) || bound == nullptr) {
        return;
    }
    auto* texture = static_cast<IDirect3DTexture9*>(bound);
    if (bound != g_texture && bound->GetType() == D3DRTYPE_TEXTURE &&
        IsSceneDepth(device, texture)) {
        Release(g_texture);
        texture->AddRef();
        g_texture = texture;
        if (++g_changes <= kLoggedChanges) {
            FCSE::Logf("depth: linear depth is texture %p", static_cast<void*>(texture));
        }
    }
    if (bound == g_texture) {
        device->GetPixelShaderConstantF(kMetreWeights, g_metreWeights, 1);
    }
    bound->Release();
}

bool SkyOverhaul::DepthTexture::Latest(Found& out) {
    if (g_texture == nullptr) {
        return false;
    }
    out.texture = g_texture;
    for (int i = 0; i < 3; i++) {
        out.metreWeights[i] = g_metreWeights[i];
    }
    return true;
}

void SkyOverhaul::DepthTexture::ReleaseDeviceObjects() {
    Release(g_texture);
}
