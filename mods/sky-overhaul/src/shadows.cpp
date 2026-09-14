#include "shadows.h"

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

#include "shadows_apply_ps.h"
#include "shadows_blur_ps.h"

#include <algorithm>

namespace {
    // Above the clouds' registers, which the shadow draw binds for itself. The last is the blur's,
    // set per blur pass.
    constexpr UINT kFirstConstant = 88;
    constexpr UINT kFrameConstants = 6;
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

    bool g_enabled = false;

    IDirect3DDevice9* g_owner = nullptr;
    // The shadows at half resolution, and the other half of their blur.
    Target g_halves[2] = {};
    UINT g_width = 0;
    UINT g_height = 0;
    // Set once the device refuses a target; cleared on reset.
    bool g_refused = false;

    uint32_t g_passesWithoutDepth = 0;

    SkyOverhaul::PixelShader g_blurShader{"shadows blur", g_shadowsBlurPixelShader};
    SkyOverhaul::PixelShader g_applyShader{"shadows apply", g_shadowsApplyPixelShader};

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
                FCSE::Logf("shadows: no %ux%u target, 0x%08lX", desc.Width, desc.Height,
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

    // The shadows at half resolution and blurred, then multiplied into the world wherever it drew
    // beyond arm's length.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& depth, float strength, float sun) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* blurShader = g_blurShader.Get(device);
        IDirect3DPixelShader9* applyShader = g_applyShader.Get(device);
        if (strength <= 0.0f || sun <= 0.0f || g_refused || blurShader == nullptr ||
            applyShader == nullptr || view.horizontalScale == 0.0f || view.verticalScale == 0.0f ||
            !EnsureTargets(device, pass.backBuffer)) {
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
            strength, sun, 0.0f, 0.0f};

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

        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_ALPHA);
        SkyOverhaul::Clouds::DrawShadow(device);
        device->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
        device->SetSamplerState(0, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_POINT);

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

void SkyOverhaul::Shadows::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const float elapsed = g_clock.Lap();

    DepthTexture::Found depth = {};
    if (!DepthTexture::Latest(depth)) {
        if (++g_passesWithoutDepth == kPatience) {
            FCSE::Logf("shadows: no draw has shown the linear depth texture, so nothing is "
                       "darkened. It needs DepthPassQuality high or above.");
        }
        return;
    }
    Camera::View view;
    CloudLayer::Lighting lighting;
    if (!Camera::Read(pass.device, view) || !CloudLayer::Latest(lighting)) {
        return;
    }
    const float sun =
        std::clamp((lighting.sunDirection[2] - kSunLow) / (kSunHigh - kSunLow), 0.0f, 1.0f);
    const bool drawn = Draw(pass, view, depth, Tuning::Current().shadowStrength, sun);

    if (g_heartbeat.Due(elapsed)) {
        FCSE::Logf("shadows f%u: sun %.2f | drawn %d", pass.frame, sun, drawn ? 1 : 0);
    }
}

void SkyOverhaul::Shadows::ReleaseDeviceObjects() {
    ReleaseTargets();
    DepthTexture::ReleaseDeviceObjects();
    g_blurShader.Release();
    g_applyShader.Release();
    g_owner = nullptr;
    g_refused = false;
}

void SkyOverhaul::Shadows::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetWatchDepth(enabled);
}
