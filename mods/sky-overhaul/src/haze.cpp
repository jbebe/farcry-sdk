#include "haze.h"

#include "depth_constants.h"
#include "sky_model.h"

#include "engine/blur_targets.h"
#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/depth_texture.h"
#include "engine/frame_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/solid_depth.h"
#include "tuning.h"

#include "haze_mask_blur_ps.h"
#include "haze_mask_ps.h"
#include "haze_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 4;
    // Set per pass of the mask's blur, after the frame's.
    constexpr UINT kAxisConstant = kOwnConstant + kOwnConstants;

    constexpr DWORD kSolidDepthSampler = 1;
    constexpr DWORD kMaskSampler = 2;

    // The sun's height, as a sine, over which the ground heats up: about 15 to 45 degrees.
    constexpr float kHeatLow = 0.26f;
    constexpr float kHeatHigh = 0.7f;

    // How far the air bends a look at full strength over 200 m of hot air, in radians: about two
    // pixels at 1080 lines.
    constexpr float kFullBend = 0.002f;

    // The sine of the steepest look onto flat ground that still shows a mirage: about 1.2 degrees.
    constexpr float kMirageSine = 0.021f;

    // How deep the shimmering air over the ground is, in metres.
    constexpr float kHotLayer = 15.0f;

    // Kept short so the shimmer's coordinates stay precise; it jumps once when the clock wraps.
    constexpr float kClockWrap = 3600.0f;

    constexpr float kRadiansPerDegree = 3.14159265f / 180.0f;

    // Below this neither changes anything a player could see.
    constexpr float kFaintest = 0.002f;

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"haze", g_hazePixelShader};
    SkyOverhaul::PixelShader g_maskShader{"haze mask", g_hazeMaskPixelShader};
    SkyOverhaul::PixelShader g_maskBlurShader{"haze mask blur", g_hazeMaskBlurPixelShader};
    // Where a mirage shows, at half resolution, and the other half of its blur.
    SkyOverhaul::BlurTargets g_mask{"haze"};
    SkyOverhaul::Stopwatch g_stopwatch;
    float g_seconds = 0.0f;

    // Draws where a mirage shows into the mask and blurs it, leaving the pass's own target, depth,
    // scissor and depth range bound again. False if the device would not make the mask.
    bool DrawMask(const SkyOverhaul::Frame::Pass& pass, SkyOverhaul::DrawGuard& draw) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* maskShader = g_maskShader.Get(device);
        IDirect3DPixelShader9* blurShader = g_maskBlurShader.Get(device);
        if (maskShader == nullptr || blurShader == nullptr ||
            !g_mask.Ensure(device, (pass.backBuffer.Width + 1) / 2,
                           (pass.backBuffer.Height + 1) / 2)) {
            return false;
        }

        // A half-resolution target cannot share the world's multisampled depth, and switching
        // targets resets the scissor rectangle the engine may be counting on.
        RECT scissor = {};
        device->GetScissorRect(&scissor);
        device->SetDepthStencilSurface(nullptr);
        device->SetRenderTarget(0, g_mask.First().surface);
        device->SetPixelShader(maskShader);
        draw.ClipQuad(0.0f);
        device->SetPixelShader(blurShader);
        g_mask.Blur(device, draw, kAxisConstant, 0.0f, 0.0f);

        device->SetRenderTarget(0, pass.target);
        device->SetDepthStencilSurface(pass.depth);
        device->SetScissorRect(&scissor);
        D3DVIEWPORT9 wholeRange = pass.viewport;
        wholeRange.MinZ = 0.0f;
        wholeRange.MaxZ = 1.0f;
        device->SetViewport(&wholeRange);
        return true;
    }
}

void SkyOverhaul::Haze::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    g_seconds = std::fmod(g_seconds + g_stopwatch.Lap(), kClockWrap);

    CloudLayer::Lighting lighting;
    DepthTexture::Found depth = {};
    Camera::View view;
    if (!CloudLayer::Latest(lighting) || !DepthTexture::Latest(depth) ||
        !Camera::Read(pass.device, view) || view.horizontalScale == 0.0f ||
        view.verticalScale == 0.0f) {
        return;
    }

    // Stateless: the ground is as hot as the sun is high, and a storm cools it.
    const float heat =
        std::clamp((lighting.sunDirection[2] - kHeatLow) / (kHeatHigh - kHeatLow), 0.0f, 1.0f) *
        (1.0f - SkyModel::Storminess(lighting.storm));
    const Tuning::Values v = Tuning::Current();
    // The mirage tells bare ground from grass by the depth without it.
    SolidDepth::SetWanted(SolidDepth::User::Haze, v.mirageStrength > 0.0f);
    SolidDepth::Found solid = {};
    const float mirage = SolidDepth::Latest(solid) ? heat * v.mirageStrength : 0.0f;
    if ((std::max)(heat * v.hazeStrength, mirage) < kFaintest) {
        return;
    }
    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    IDirect3DTexture9* world = shader != nullptr ? FrameCopy::Take(pass) : nullptr;
    if (world == nullptr) {
        return;
    }

    ScreenDraw draw(pass.device, kFirstConstant, kAxisConstant + 1 - kFirstConstant);
    DepthConstants::Set(pass.device, view, depth, pass.backBuffer.Width, pass.backBuffer.Height);
    float projection[2];
    SolidDepth::Projection(view, projection);
    const float slope = v.mirageSlope * kRadiansPerDegree;
    const float maskWidth =static_cast<float>((pass.backBuffer.Width + 1) / 2);
    const float maskHeight = static_cast<float>((pass.backBuffer.Height + 1) / 2);
    float constants[kOwnConstants * 4] = {
        heat * v.hazeStrength * kFullBend, mirage, kMirageSine, kHotLayer,
        g_seconds, 0.0f, 0.0f, 0.0f,
        projection[0], projection[1], 1.0f / maskWidth, 1.0f / maskHeight,
        std::cos(slope), std::cos(0.5f * slope), 0.0f, 0.0f};
    pass.device->SetPixelShaderConstantF(kOwnConstant, constants, kOwnConstants);
    pass.device->SetTexture(kSolidDepthSampler, solid.texture);

    if (mirage < kFaintest || !DrawMask(pass, draw)) {
        constants[1] = 0.0f;
        pass.device->SetPixelShaderConstantF(kOwnConstant, constants, 1);
    }

    // The world target's alpha carries brightness for the bloom.
    pass.device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                            D3DCOLORWRITEENABLE_GREEN |
                                                            D3DCOLORWRITEENABLE_BLUE);
    pass.device->SetPixelShader(shader);
    pass.device->SetTexture(0, world);
    pass.device->SetTexture(kMaskSampler, g_mask.First().texture);
    // The bend moves a look by fractions of a pixel, and the mask is half the size.
    for (DWORD sampler : {DWORD{0}, kMaskSampler}) {
        pass.device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        pass.device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    }
    draw.ClipQuad(0.0f);
}

void SkyOverhaul::Haze::ReleaseDeviceObjects() {
    g_mask.Release();
    g_shader.Release();
    g_maskShader.Release();
    g_maskBlurShader.Release();
}

void SkyOverhaul::Haze::SetEnabled(bool enabled) {
    g_enabled = enabled;
    SolidDepth::SetWanted(SolidDepth::User::Haze, enabled);
}
