// The scope's shadow. The lens is found as the hole the housing leaves in the gun's depth, and a
// clear disc of its size, pushed off its centre by the hand's drift, leaves the rest of it dark.
#include "scope_shadow.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "scope_lens_ps.h"
#include "scope_radius_ps.h"
#include "scope_shadow_ps.h"

#include <atomic>
#include <chrono>
#include <cmath>

namespace {
    // RadiusPS's directions and the radii it looks between, in screen heights.
    constexpr UINT kDirections = 24;
    constexpr float kNearest = 0.05f;
    constexpr float kFarthest = 0.6f;
    constexpr float kSteps = 48.0f;
    // The smallest radius taken for a lens; anything smaller is the gun itself, not a hole in it.
    constexpr float kSmallestLens = 0.1f;

    // How far the clear part of the lens moves per metre of drift, in lens radii, how wide the
    // shadow's soft edge is, in lens radii, and how dark it gets.
    constexpr float kShiftPerMetre = 70.0f;
    constexpr float kSoftEdge = 0.12f;
    constexpr float kDarkness = 0.95f;
    // Seconds for the lens radius to follow most of the way.
    constexpr float kFollow = 0.1f;

    constexpr UINT kShadow = 0;
    constexpr UINT kConstantCount = 3;

    constexpr D3DFORMAT kRadiusFormat = D3DFMT_R32F;

    WeaponOverhaul::PixelShader g_radiusShader{"scope radius", g_scopeRadiusPixelShader};
    WeaponOverhaul::PixelShader g_lensShader{"scope lens", g_scopeLensPixelShader};
    WeaponOverhaul::PixelShader g_shadowShader{"scope shadow", g_scopeShadowPixelShader};

    std::atomic<bool> g_enabled{true};
    IDirect3DDevice9* g_owner = nullptr;
    // The first housing hit per direction, and the lens radius, this frame's and the last.
    WeaponOverhaul::Target g_radii;
    WeaponOverhaul::Target g_lens[2];
    int g_lensIndex = 0;
    bool g_lensFresh = true;
    std::chrono::steady_clock::time_point g_lastDraw;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;
    bool g_reported = false;
    IDirect3DSurface9* g_readback = nullptr;

    void ReleaseTargets() {
        WeaponOverhaul::Release(g_radii);
        for (WeaponOverhaul::Target& lens : g_lens) {
            WeaponOverhaul::Release(lens);
        }
        g_lensFresh = true;
    }

    bool Create(IDirect3DDevice9* device, UINT width, WeaponOverhaul::Target& target) {
        const HRESULT created =
            WeaponOverhaul::CreateTarget(device, width, 1, kRadiusFormat, target);
        if (FAILED(created)) {
            ReleaseTargets();
            g_refused = true;
            FCSE::Logf("scope shadow: the device refused a %ux1 target, 0x%08lX", width,
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
        return Create(device, kDirections, g_radii) && Create(device, 1, g_lens[0]) &&
               Create(device, 1, g_lens[1]);
    }

    // How far the lens radius moves toward this frame's, from the time since the last.
    float Follow() {
        const auto now = std::chrono::steady_clock::now();
        const float seconds = std::chrono::duration<float>(now - g_lastDraw).count();
        g_lastDraw = now;
        return 1.0f - std::exp(-seconds / kFollow);
    }

    // The lens radius as measured, once, the first time a scope has settled.
    void Report(IDirect3DDevice9* device, const WeaponOverhaul::Target& lens) {
        D3DLOCKED_RECT locked = {};
        if (FAILED(device->CreateOffscreenPlainSurface(1, 1, kRadiusFormat, D3DPOOL_SYSTEMMEM,
                                                       &g_readback, nullptr)) ||
            FAILED(device->GetRenderTargetData(lens.surface, g_readback)) ||
            FAILED(g_readback->LockRect(&locked, nullptr, D3DLOCK_READONLY))) {
            FCSE::ApiPointer()->Log("scope shadow: the lens radius could not be read back");
        } else {
            const float radius = *static_cast<const float*>(locked.pBits);
            g_readback->UnlockRect();
            FCSE::Logf("scope shadow: the lens radius is %.3f of the screen's height%s", radius,
                       radius < kSmallestLens ? ", too small, so there is no shadow" : "");
        }
        WeaponOverhaul::Release(g_readback);
    }

    void Draw(const WeaponOverhaul::Frame::Pass& pass,
              const WeaponOverhaul::WeaponDraws::Depth& depth, float scoped) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* radius = g_radiusShader.Get(device);
        IDirect3DPixelShader9* lens = g_lensShader.Get(device);
        IDirect3DPixelShader9* shadow = g_shadowShader.Get(device);
        if (radius == nullptr || lens == nullptr || shadow == nullptr || !EnsureTargets(device)) {
            return;
        }

        const float width = static_cast<float>(WeaponOverhaul::Frame::Width());
        const float height = static_cast<float>(WeaponOverhaul::Frame::Height());
        const WeaponOverhaul::Aim::Offset drift = WeaponOverhaul::Aim::Drift();
        // The rifle turning right carries the eyepiece right of the eye, which leaves the left of
        // the lens dark: the clear part moves with the turn, up the screen as it turns up.
        const float constants[kConstantCount * 4] = {
            drift.right * kShiftPerMetre, -drift.up * kShiftPerMetre, scoped, width / height,
            kNearest, (kFarthest - kNearest) / kSteps, height / width, Follow(),
            kSoftEdge, kDarkness, kSmallestLens, 0.0f,
        };
        const WeaponOverhaul::Target& lensNow = g_lens[g_lensIndex];
        const WeaponOverhaul::Target& lensBefore = g_lens[g_lensIndex ^ 1];

        {
            WeaponOverhaul::ScreenDraw draw(device, kShadow, kConstantCount);
            device->SetPixelShaderConstantF(kShadow, constants, kConstantCount);
            // A target of our own cannot share the scene's multisampled depth.
            device->SetDepthStencilSurface(nullptr);

            if (g_lensFresh) {
                for (const WeaponOverhaul::Target& target : g_lens) {
                    device->SetRenderTarget(0, target.surface);
                    device->Clear(0, nullptr, D3DCLEAR_TARGET, 0, 1.0f, 0);
                }
                g_lensFresh = false;
            }
            device->SetRenderTarget(0, g_radii.surface);
            device->SetTexture(3, depth.texture);
            device->SetPixelShader(radius);
            draw.Quad(0.0f, 0.0f, static_cast<float>(kDirections), 1.0f);

            device->SetRenderTarget(0, lensNow.surface);
            device->SetTexture(0, g_radii.texture);
            device->SetTexture(5, lensBefore.texture);
            device->SetPixelShader(lens);
            draw.Quad(0.0f, 0.0f, 1.0f, 1.0f);

            device->SetRenderTarget(0, pass.target);
            device->SetTexture(5, lensNow.texture);
            device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
            device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
            device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
            device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                               D3DCOLORWRITEENABLE_GREEN |
                                                               D3DCOLORWRITEENABLE_BLUE);
            device->SetPixelShader(shadow);
            draw.Quad(0.0f, 0.0f, width, height);
        }

        if (!g_reported && scoped >= 1.0f) {
            g_reported = true;
            Report(device, lensNow);
        }
        g_lensIndex ^= 1;
    }
}

void WeaponOverhaul::ScopeShadow::OnGunPass(const Frame::Pass& pass,
                                            const WeaponDraws::Depth& depth) {
    const float scoped = g_enabled ? Aim::Scoped() : 0.0f;
    if (scoped > 0.0f) {
        Draw(pass, depth, scoped);
    }
}

void WeaponOverhaul::ScopeShadow::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::ScopeShadow::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
    g_radiusShader.Release();
    g_lensShader.Release();
    g_shadowShader.Release();
}
