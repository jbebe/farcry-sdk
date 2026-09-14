#include "occlusion.h"

#include "clouds.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/depth_texture.h"
#include "engine/dome_draw.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "occlusion_ambient_ps.h"
#include "occlusion_apply_ps.h"
#include "occlusion_blur_ps.h"

#include <algorithm>

namespace {
    using SkyOverhaul::Tuning::Values;

    // Above the clouds' registers, which the shadow draw binds for itself. The last is the blur's,
    // set per blur pass.
    constexpr UINT kFirstConstant = 88;
    constexpr UINT kFrameConstants = 7;
    constexpr UINT kBlurConstant = kFirstConstant + kFrameConstants;

    constexpr DWORD kDepthSampler = 3;

    // Nothing nearer than this is darkened, which keeps the player's own weapon and hands clean.
    constexpr float kNearest = 1.0f;
    // The share of the view distance past which the depth is the engine's clear, not geometry.
    constexpr float kFarthestShare = 0.995f;
    // The sun's height, as a sine, over which its shadows come in.
    constexpr float kSunLow = 0.05f;
    constexpr float kSunHigh = 0.15f;
    // Sky passes without the depth texture before that is reported.
    constexpr uint32_t kPatience = 300;

    // The shaders read their place from the rasteriser, so the quad's rays are unused.
    constexpr float kNoRays[4][3] = {};

    struct Target {
        IDirect3DTexture9* texture;
        IDirect3DSurface9* surface;
    };

    bool g_ambient = false;
    bool g_shadows = false;

    IDirect3DDevice9* g_owner = nullptr;
    // The occlusion at half resolution, and the other half of its blur.
    Target g_halves[2] = {};
    UINT g_width = 0;
    UINT g_height = 0;
    // Set once the device refuses a target; cleared on reset.
    bool g_refused = false;

    uint32_t g_passesWithoutDepth = 0;

    SkyOverhaul::PixelShader g_ambientShader{"occlusion", g_occlusionAmbientPixelShader};
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

    // Both halves of the blur at half the back buffer's size, made on first use.
    bool EnsureTargets(IDirect3DDevice9* device, const D3DSURFACE_DESC& backBuffer) {
        if (g_owner != device) {
            ReleaseTargets();
            g_owner = device;
        }
        if (g_halves[0].texture != nullptr && g_width == backBuffer.Width &&
            g_height == backBuffer.Height) {
            return true;
        }
        ReleaseTargets();
        D3DSURFACE_DESC desc = {};
        desc.Width = (backBuffer.Width + 1) / 2;
        desc.Height = (backBuffer.Height + 1) / 2;
        desc.Format = D3DFMT_A8R8G8B8;
        for (Target& target : g_halves) {
            const HRESULT created =
                SkyOverhaul::CreateTarget(device, desc, &target.texture, &target.surface);
            if (FAILED(created)) {
                ReleaseTargets();
                g_refused = true;
                FCSE::Logf("occlusion: no %ux%u target, 0x%08lX", desc.Width, desc.Height,
                           static_cast<unsigned long>(created));
                return false;
            }
        }
        g_width = backBuffer.Width;
        g_height = backBuffer.Height;
        return true;
    }

    // The depth buffer's value for a point `metres` along the camera's axis.
    float BufferDepth(const SkyOverhaul::Camera::View& view, float metres) {
        const float* m = view.viewProjection;
        float p[3];
        for (int i = 0; i < 3; i++) {
            p[i] = view.eye[i] + view.direction[i] * metres;
        }
        const float z = m[8] * p[0] + m[9] * p[1] + m[10] * p[2] + m[11];
        const float w = m[12] * p[0] + m[13] * p[1] + m[14] * p[2] + m[15];
        return w > 0.0f ? z / w : 0.0f;
    }

    void BlurInto(IDirect3DDevice9* device, SkyOverhaul::DrawGuard& draw, const Target& from,
                  const Target& into, float axisX, float axisY) {
        const float blur[4] = {axisX, axisY, 1.0f / static_cast<float>((g_width + 1) / 2),
                               1.0f / static_cast<float>((g_height + 1) / 2)};
        device->SetPixelShaderConstantF(kBlurConstant, blur, 1);
        // Before the target changes, so `into` is never bound to be read and written at once.
        device->SetTexture(0, from.texture);
        device->SetRenderTarget(0, into.surface);
        draw.ClipQuad(0.0f, kNoRays);
    }

    // The occlusion and the shadows at half resolution and blurred, then multiplied into the world
    // wherever it drew beyond arm's length.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& depth, const Values& v, float sun) {
        IDirect3DDevice9* device = pass.device;
        const bool ambient = g_ambient && v.occlusionStrength > 0.0f;
        const bool shadowed = g_shadows && v.shadowStrength > 0.0f && sun > 0.0f;
        IDirect3DPixelShader9* ambientShader = g_ambientShader.Get(device);
        IDirect3DPixelShader9* blurShader = g_blurShader.Get(device);
        IDirect3DPixelShader9* applyShader = g_applyShader.Get(device);
        if ((!ambient && !shadowed) || g_refused || ambientShader == nullptr ||
            blurShader == nullptr || applyShader == nullptr || view.horizontalScale == 0.0f ||
            view.verticalScale == 0.0f || !EnsureTargets(device, pass.backBuffer)) {
            return false;
        }

        const float constants[kFrameConstants * 4] = {
            depth.metreWeights[0], depth.metreWeights[1], depth.metreWeights[2],
            view.viewDistance * kFarthestShare,
            1.0f / view.horizontalScale, 1.0f / view.verticalScale,
            1.0f / static_cast<float>(g_width), 1.0f / static_cast<float>(g_height),
            view.right[0], view.right[1], view.right[2], 0.0f,
            view.up[0], view.up[1], view.up[2], 0.0f,
            view.direction[0], view.direction[1], view.direction[2], 0.0f,
            v.occlusionRadius, v.occlusionStrength, v.occlusionFade, 0.0f,
            v.shadowStrength, sun, 0.0f, 0.0f};

        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kFrameConstants + 1);
        device->SetPixelShaderConstantF(kFirstConstant, constants, kFrameConstants);
        device->SetTexture(kDepthSampler, depth.texture);

        // A half-resolution target cannot share the world's multisampled depth, and switching
        // targets resets the scissor rectangle the engine may be counting on.
        IDirect3DSurface9* sceneDepth = nullptr;
        device->GetDepthStencilSurface(&sceneDepth);
        RECT scissor = {};
        device->GetScissorRect(&scissor);
        device->SetDepthStencilSurface(nullptr);
        device->SetRenderTarget(0, g_halves[0].surface);
        device->Clear(0, nullptr, D3DCLEAR_TARGET, 0xFFFFFFFF, 1.0f, 0);

        if (ambient) {
            device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED);
            device->SetPixelShader(ambientShader);
            draw.ClipQuad(0.0f, kNoRays);
        }
        if (shadowed) {
            device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_ALPHA);
            SkyOverhaul::Clouds::DrawShadow(device);
            device->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
            device->SetSamplerState(0, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
            device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
            device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_POINT);
        }

        device->SetRenderState(D3DRS_COLORWRITEENABLE,
                               D3DCOLORWRITEENABLE_RED | D3DCOLORWRITEENABLE_ALPHA);
        device->SetPixelShader(blurShader);
        BlurInto(device, draw, g_halves[0], g_halves[1], 1.0f, 0.0f);
        BlurInto(device, draw, g_halves[1], g_halves[0], 0.0f, 1.0f);

        device->SetRenderTarget(0, pass.target);
        device->SetDepthStencilSurface(sceneDepth);
        SkyOverhaul::Release(sceneDepth);
        device->SetScissorRect(&scissor);
        D3DVIEWPORT9 wholeRange = pass.viewport;
        wholeRange.MinZ = 0.0f;
        wholeRange.MaxZ = 1.0f;
        device->SetViewport(&wholeRange);

        // Passes the depth test only where the world drew beyond arm's length, and the sky, which
        // the shader leaves alone. The world target's alpha carries brightness for the bloom.
        device->SetTexture(0, g_halves[0].texture);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ZFUNC, D3DCMP_LESS);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetPixelShader(applyShader);
        return draw.ClipQuad(BufferDepth(view, kNearest), kNoRays);
    }
}

void SkyOverhaul::Occlusion::OnScenePass(const Frame::Pass& pass) {
    if ((!g_ambient && !g_shadows) || !pass.sky || !pass.live) {
        return;
    }
    const float elapsed = g_clock.Lap();

    DepthTexture::Found depth = {};
    if (!DepthTexture::Latest(depth)) {
        if (++g_passesWithoutDepth == kPatience) {
            FCSE::Logf("occlusion: no draw has shown the linear depth texture, so nothing is "
                       "darkened. It needs DepthPassQuality high or above.");
        }
        return;
    }
    Camera::View view;
    CloudLayer::Lighting lighting;
    if (!Camera::Read(pass.device, view) || !CloudLayer::Latest(lighting)) {
        return;
    }
    const Values v = Tuning::Current();
    const float sun =
        std::clamp((lighting.sunDirection[2] - kSunLow) / (kSunHigh - kSunLow), 0.0f, 1.0f);
    const bool drawn = Draw(pass, view, depth, v, sun);

    if (g_heartbeat.Due(elapsed)) {
        FCSE::Logf("occlusion f%u: ambient %d shadows %d | sun %.2f | drawn %d", pass.frame,
                   g_ambient ? 1 : 0, g_shadows ? 1 : 0, sun, drawn ? 1 : 0);
    }
}

void SkyOverhaul::Occlusion::ReleaseDeviceObjects() {
    ReleaseTargets();
    DepthTexture::ReleaseDeviceObjects();
    g_ambientShader.Release();
    g_blurShader.Release();
    g_applyShader.Release();
    g_owner = nullptr;
    g_refused = false;
}

void SkyOverhaul::Occlusion::SetAmbientEnabled(bool enabled) {
    g_ambient = enabled;
    DomeDraw::SetWatchDepth(g_ambient || g_shadows);
}

void SkyOverhaul::Occlusion::SetShadowsEnabled(bool enabled) {
    g_shadows = enabled;
    DomeDraw::SetWatchDepth(g_ambient || g_shadows);
}
