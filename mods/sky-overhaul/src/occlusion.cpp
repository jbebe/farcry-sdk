#include "occlusion.h"

#include "depth_constants.h"
#include "tuning.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/com.h"
#include "engine/depth_texture.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/solid_depth.h"
#include "fcse_api.h"

#include "occlusion_apply_ps.h"
#include "occlusion_blur_ps.h"
#include "occlusion_gtao_ps.h"

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 3;
    // Set per blur pass, after the frame's.
    constexpr UINT kBlurConstant = kOwnConstant + kOwnConstants;

    constexpr DWORD kSolidDepthSampler = 1;

    // The shaders read their place from the rasteriser, so the quad's rays are unused.
    constexpr float kNoRays[4][3] = {};

    struct Target {
        IDirect3DTexture9* texture;
        IDirect3DSurface9* surface;
    };

    bool g_enabled = false;

    IDirect3DDevice9* g_owner = nullptr;
    // The occlusion at half resolution, and the other half of its blur.
    Target g_halves[2] = {};
    UINT g_width = 0;
    UINT g_height = 0;
    // Set once the device refuses a target; cleared on reset.
    bool g_refused = false;

    SkyOverhaul::PixelShader g_gtaoShader{"occlusion", g_occlusionGtaoPixelShader};
    SkyOverhaul::PixelShader g_blurShader{"occlusion blur", g_occlusionBlurPixelShader};
    SkyOverhaul::PixelShader g_applyShader{"occlusion apply", g_occlusionApplyPixelShader};

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{2.0f};

    void ReleaseTargets() {
        for (Target& target : g_halves) {
            SkyOverhaul::Release(target.surface);
            SkyOverhaul::Release(target.texture);
        }
    }

    // Both halves of the blur, the size of the solid depth, made on first use.
    bool EnsureTargets(IDirect3DDevice9* device, UINT width, UINT height) {
        if (g_owner == device && g_halves[0].texture != nullptr && g_width == width &&
            g_height == height) {
            return true;
        }
        ReleaseTargets();
        g_owner = device;
        D3DSURFACE_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.Format = D3DFMT_A8R8G8B8;
        for (Target& target : g_halves) {
            const HRESULT created =
                SkyOverhaul::CreateTarget(device, desc, &target.texture, &target.surface);
            if (FAILED(created)) {
                ReleaseTargets();
                g_refused = true;
                FCSE::Logf("occlusion: no %ux%u target, 0x%08lX", width, height,
                           static_cast<unsigned long>(created));
                return false;
            }
        }
        g_width = width;
        g_height = height;
        return true;
    }

    void BlurInto(IDirect3DDevice9* device, SkyOverhaul::DrawGuard& draw, const Target& from,
                  const Target& into, float axisX, float axisY) {
        const float blur[4] = {axisX, axisY, 0.0f, 0.0f};
        device->SetPixelShaderConstantF(kBlurConstant, blur, 1);
        // Before the target changes, so `into` is never bound to be read and written at once.
        device->SetTexture(0, from.texture);
        device->SetRenderTarget(0, into.surface);
        draw.ClipQuad(0.0f, kNoRays);
    }

    // The occlusion at half resolution and blurred, then multiplied into the world.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& engine,
              const SkyOverhaul::SolidDepth::Found& solid, const SkyOverhaul::Tuning::Values& v) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* gtaoShader = g_gtaoShader.Get(device);
        IDirect3DPixelShader9* blurShader = g_blurShader.Get(device);
        IDirect3DPixelShader9* applyShader = g_applyShader.Get(device);
        if (g_refused || gtaoShader == nullptr || blurShader == nullptr ||
            applyShader == nullptr || view.horizontalScale == 0.0f || view.verticalScale == 0.0f ||
            !EnsureTargets(device, solid.width, solid.height)) {
            return false;
        }

        // The solid depth's linearisation is written with w's sign folded in, and the pixels a
        // metre covers are given for one metre away.
        const float own[kOwnConstants * 4] = {
            view.depthScale * view.wFromDepth, view.depthOffset, 0.0f, v.occlusionRootReach,
            v.occlusionRadius, v.occlusionScreenLimit, v.occlusionFadeDistance, v.occlusionStrength,
            1.0f / static_cast<float>(solid.width), 1.0f / static_cast<float>(solid.height),
            0.5f * view.verticalScale * static_cast<float>(solid.height), 0.0f};

        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kBlurConstant + 1 - kFirstConstant);
        SkyOverhaul::DepthConstants::Set(device, view, engine, pass.backBuffer.Width,
                                         pass.backBuffer.Height);
        device->SetPixelShaderConstantF(kOwnConstant, own, kOwnConstants);
        device->SetTexture(kSolidDepthSampler, solid.texture);

        // A half-resolution target cannot share the world's multisampled depth, and switching
        // targets resets the scissor rectangle the engine may be counting on.
        RECT scissor = {};
        device->GetScissorRect(&scissor);
        device->SetDepthStencilSurface(nullptr);
        device->SetRenderTarget(0, g_halves[0].surface);
        device->SetPixelShader(gtaoShader);
        draw.ClipQuad(0.0f, kNoRays);

        device->SetPixelShader(blurShader);
        BlurInto(device, draw, g_halves[0], g_halves[1], 1.0f, 0.0f);
        BlurInto(device, draw, g_halves[1], g_halves[0], 0.0f, 1.0f);

        device->SetRenderTarget(0, pass.target);
        device->SetDepthStencilSurface(pass.depth);
        device->SetScissorRect(&scissor);
        D3DVIEWPORT9 wholeRange = pass.viewport;
        wholeRange.MinZ = 0.0f;
        wholeRange.MaxZ = 1.0f;
        device->SetViewport(&wholeRange);

        // Passes the depth test only where the world drew nearer than the occlusion fades out by.
        // The world target's alpha carries brightness for the bloom, so it is left alone.
        device->SetTexture(0, g_halves[0].texture);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ZFUNC, D3DCMP_GREATER);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetPixelShader(applyShader);
        return draw.ClipQuad(SkyOverhaul::Camera::BufferDepth(view, v.occlusionFadeDistance),
                             kNoRays);
    }
}

void SkyOverhaul::Occlusion::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const Tuning::Values v = Tuning::Current();
    const bool wanted = v.occlusionStrength > 0.0f;
    SolidDepth::SetEnabled(wanted);

    SolidDepth::Found solid = {};
    DepthTexture::Found engine = {};
    Camera::View view;
    const bool drawn = wanted && SolidDepth::Latest(solid) && DepthTexture::Latest(engine) &&
                       Camera::Read(pass.device, view) && Draw(pass, view, engine, solid, v);

    if (g_heartbeat.Due(g_clock.Lap())) {
        FCSE::Logf("occlusion f%u: drawn %d", pass.frame, drawn ? 1 : 0);
    }
}

void SkyOverhaul::Occlusion::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_gtaoShader.Release();
    g_blurShader.Release();
    g_applyShader.Release();
    g_owner = nullptr;
    g_refused = false;
}

void SkyOverhaul::Occlusion::SetEnabled(bool enabled) {
    g_enabled = enabled;
    SolidDepth::SetEnabled(enabled);
}
