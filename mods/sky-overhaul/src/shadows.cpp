#include "shadows.h"

#include "clouds.h"
#include "depth_constants.h"

#include "engine/blur_targets.h"
#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/depth_texture.h"
#include "engine/dome_draw.h"
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
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kFrameConstants = SkyOverhaul::DepthConstants::kCount + 1;
    constexpr UINT kBlurConstant = kFirstConstant + kFrameConstants;

    // The sun's height, as a sine, over which its shadows come in.
    constexpr float kSunLow = 0.05f;
    constexpr float kSunHigh = 0.15f;
    // Sky passes without the depth texture before that is reported.
    constexpr uint32_t kPatience = 300;

    bool g_enabled = false;

    // The shadows at half resolution, and the other half of their blur.
    SkyOverhaul::BlurTargets g_halves{"shadows"};

    uint32_t g_passesWithoutDepth = 0;

    SkyOverhaul::PixelShader g_blurShader{"shadows blur", g_shadowsBlurPixelShader};
    SkyOverhaul::PixelShader g_applyShader{"shadows apply", g_shadowsApplyPixelShader};

    // The shadows at half resolution and blurred, then multiplied into the world.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& depth, float strength, float sun) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* blurShader = g_blurShader.Get(device);
        IDirect3DPixelShader9* applyShader = g_applyShader.Get(device);
        if (strength <= 0.0f || sun <= 0.0f || blurShader == nullptr || applyShader == nullptr ||
            view.horizontalScale == 0.0f || view.verticalScale == 0.0f ||
            !g_halves.Ensure(device, (pass.backBuffer.Width + 1) / 2,
                             (pass.backBuffer.Height + 1) / 2)) {
            return false;
        }

        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kFrameConstants + 1);
        SkyOverhaul::DepthConstants::Set(device, view, depth, pass.backBuffer.Width,
                                         pass.backBuffer.Height);
        const float shade[4] = {strength, sun, 0.0f, 0.0f};
        device->SetPixelShaderConstantF(kFirstConstant + SkyOverhaul::DepthConstants::kCount, shade,
                                        1);

        {
            SkyOverhaul::AwayFromPass away(pass);
            device->SetRenderTarget(0, g_halves.First().surface);
            device->Clear(0, nullptr, D3DCLEAR_TARGET, 0xFFFFFFFF, 1.0f, 0);

            device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_ALPHA);
            SkyOverhaul::Clouds::DrawShadow(device);
            device->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
            device->SetSamplerState(0, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
            device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
            device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_POINT);

            device->SetPixelShader(blurShader);
            g_halves.Blur(device, draw, kBlurConstant);
        }

        // The shader leaves the sky and the weapon alone, which the engine's depth holds nothing
        // for. The world target's alpha carries brightness for the bloom.
        device->SetTexture(0, g_halves.First().texture);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetPixelShader(applyShader);
        return draw.ClipQuad(0.0f);
    }
}

void SkyOverhaul::Shadows::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }

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
    Draw(pass, view, depth, Tuning::Current().shadowStrength, sun);
}

void SkyOverhaul::Shadows::ReleaseDeviceObjects() {
    g_halves.Release();
    g_blurShader.Release();
    g_applyShader.Release();
}

void SkyOverhaul::Shadows::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetWatchDepth(DomeDraw::DepthUser::Shadows, enabled);
}
