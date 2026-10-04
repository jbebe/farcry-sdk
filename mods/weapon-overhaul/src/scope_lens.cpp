// RadiusPS casts rays out from where the lens was last found; LensPS fits a circle to their hits.
#include "scope_lens.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "scope_lens_ps.h"
#include "scope_radius_ps.h"

#include <chrono>
#include <cmath>

namespace {
    // RadiusPS's directions and the radii it looks between, in screen heights.
    constexpr UINT kDirections = 24;
    constexpr float kNearest = 0.05f;
    constexpr float kFarthest = 1.0f;
    constexpr float kSteps = 64.0f;
    // Seconds for the lens radius to follow most of the way.
    constexpr float kFollow = 0.1f;

    constexpr UINT kConstantCount = 2;

    constexpr D3DFORMAT kRadiusFormat = D3DFMT_R32F;
    constexpr D3DFORMAT kLensFormat = D3DFMT_A32B32G32R32F;

    WeaponOverhaul::PixelShader g_radiusShader{"scope radius", g_scopeRadiusPixelShader};
    WeaponOverhaul::PixelShader g_lensShader{"scope lens", g_scopeLensPixelShader};

    IDirect3DDevice9* g_owner = nullptr;
    // The first housing hit per direction, and the lens, this frame's and the last.
    WeaponOverhaul::Target g_radii;
    WeaponOverhaul::Target g_lens[2];
    int g_lensIndex = 0;
    bool g_lensFresh = true;
    uint32_t g_foundFrame = WeaponOverhaul::Frame::kNever;
    std::chrono::steady_clock::time_point g_lastTrack;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;

    void ReleaseTargets() {
        WeaponOverhaul::Release(g_radii);
        for (WeaponOverhaul::Target& lens : g_lens) {
            WeaponOverhaul::Release(lens);
        }
        g_lensFresh = true;
        g_foundFrame = WeaponOverhaul::Frame::kNever;
    }

    bool Create(IDirect3DDevice9* device, UINT width, D3DFORMAT format,
                WeaponOverhaul::Target& target) {
        const HRESULT created = WeaponOverhaul::CreateTarget(device, width, 1, format, target);
        if (FAILED(created)) {
            ReleaseTargets();
            g_refused = true;
            FCSE::Logf("scope lens: the device refused a %ux1 target, 0x%08lX", width,
                       static_cast<unsigned long>(created));
            return false;
        }
        return true;
    }

    bool EnsureTargets(IDirect3DDevice9* device) {
        if (g_owner == device && g_radii.texture != nullptr) {
            return true;
        }
        ReleaseTargets();
        if (g_refused) {
            return false;
        }
        g_owner = device;
        return Create(device, kDirections, kRadiusFormat, g_radii) &&
               Create(device, 1, kLensFormat, g_lens[0]) &&
               Create(device, 1, kLensFormat, g_lens[1]);
    }

    // How far the lens radius moves toward this frame's, from the time since the last.
    float Follow() {
        const auto now = std::chrono::steady_clock::now();
        const float seconds = std::chrono::duration<float>(now - g_lastTrack).count();
        g_lastTrack = now;
        return 1.0f - std::exp(-seconds / kFollow);
    }
}

void WeaponOverhaul::ScopeLens::Track(const Frame::Pass& pass, const WeaponDraws::Depth& depth,
                                      float hole, float opening) {
    if (Aim::Scoped() <= 0.0f) {
        return;
    }
    IDirect3DDevice9* device = pass.device;
    IDirect3DPixelShader9* radius = g_radiusShader.Get(device);
    IDirect3DPixelShader9* lens = g_lensShader.Get(device);
    if (radius == nullptr || lens == nullptr || !EnsureTargets(device)) {
        return;
    }

    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float constants[kConstantCount * 4] = {
        kNearest, (kFarthest - kNearest) / kSteps, height / width, Follow(),
        hole, kSmallest, opening, 0.0f,
    };
    const Target& lensNow = g_lens[g_lensIndex];
    const Target& lensBefore = g_lens[g_lensIndex ^ 1];

    ScreenDraw draw(device, 0, kConstantCount);
    device->SetPixelShaderConstantF(0, constants, kConstantCount);
    // A target of our own cannot share the scene's multisampled depth.
    device->SetDepthStencilSurface(nullptr);

    if (g_lensFresh) {
        for (const Target& target : g_lens) {
            device->SetRenderTarget(0, target.surface);
            device->Clear(0, nullptr, D3DCLEAR_TARGET, 0, 1.0f, 0);
        }
        g_lensFresh = false;
    }
    device->SetRenderTarget(0, g_radii.surface);
    device->SetTexture(3, depth.texture);
    device->SetTexture(5, lensBefore.texture);
    device->SetPixelShader(radius);
    if (!draw.FullQuad()) {
        return;
    }

    device->SetRenderTarget(0, lensNow.surface);
    device->SetTexture(0, g_radii.texture);
    device->SetPixelShader(lens);
    draw.FullQuad();

    g_lensIndex ^= 1;
    g_foundFrame = Frame::Number();
}

IDirect3DTexture9* WeaponOverhaul::ScopeLens::Found() {
    return g_foundFrame == Frame::Number() ? g_lens[g_lensIndex ^ 1].texture : nullptr;
}

void WeaponOverhaul::ScopeLens::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
    g_radiusShader.Release();
    g_lensShader.Release();
}
