#include "haze.h"

#include "depth_constants.h"
#include "sky_model.h"

#include "engine/blur_targets.h"
#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/depth_texture.h"
#include "engine/frame_copy.h"
#include "engine/noise.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/solid_depth.h"
#include "tuning.h"

#include "haze_blur_ps.h"
#include "haze_down_ps.h"
#include "haze_mask_ps.h"
#include "haze_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 5;
    // Set per pass of a blur, after the frame's.
    constexpr UINT kAxisConstant = kOwnConstant + kOwnConstants;

    // Where in the frame's constants the mirage's and the far blur's strengths sit, in floats.
    constexpr size_t kMirageStrength = 1;
    constexpr size_t kFarStrength = 4 * 4 + 2;

    constexpr DWORD kSolidDepthSampler = 1;
    constexpr DWORD kMaskSampler = 2;
    constexpr DWORD kBlurredSampler = 4;
    constexpr DWORD kTurbulenceSampler = 5;

    // The sun's height, as a sine, over which the ground heats up: about 15 to 45 degrees.
    constexpr float kHeatLow = 0.26f;
    constexpr float kHeatHigh = 0.7f;

    // How far the air bends a look at full strength over 200 m of hot air, in radians: about two
    // pixels at 1080 lines.
    constexpr float kFullBend = 0.002f;

    // A standing eye's height over the ground, in metres, which turns the distance a mirage starts
    // at into the steepest look onto level ground that shows one.
    constexpr float kStandingEye = 1.8f;

    // How far off the waves are the size the wave sliders give, in metres.
    constexpr float kWaveReference = 100.0f;

    // Kept short so the shimmer's coordinates stay precise; it jumps once when the clock wraps.
    constexpr float kClockWrap = 3600.0f;

    constexpr float kRadiansPerDegree = 3.14159265f / 180.0f;
    constexpr float kRadiansPerArcMinute = kRadiansPerDegree / 60.0f;

    // Below this none of it changes anything a player could see.
    constexpr float kFaintest = 0.002f;

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"haze", g_hazePixelShader};
    SkyOverhaul::PixelShader g_maskShader{"haze mask", g_hazeMaskPixelShader};
    SkyOverhaul::PixelShader g_downShader{"haze down", g_hazeDownPixelShader};
    SkyOverhaul::PixelShader g_blurShader{"haze blur", g_hazeBlurPixelShader};
    // Where a mirage shows, at half resolution, and the other half of its blur.
    SkyOverhaul::BlurTargets g_mask{"haze mask"};
    // The far world at quarter resolution, in a format that keeps what is brighter than white.
    SkyOverhaul::BlurTargets g_blurred{"haze far blur"};
    SkyOverhaul::Stopwatch g_stopwatch;
    float g_seconds = 0.0f;
    // The widest view seen, as one over the tangent of half its height: the unzoomed one.
    float g_widest = 0.0f;

    void BlurFrom(IDirect3DDevice9* device, SkyOverhaul::DrawGuard& draw,
                  SkyOverhaul::BlurTargets& targets) {
        targets.Blur(device, draw, kAxisConstant, 1.0f / static_cast<float>(targets.Width()),
                     1.0f / static_cast<float>(targets.Height()));
    }

    // Where a mirage shows, into the mask, blurred.
    void DrawMask(IDirect3DDevice9* device, SkyOverhaul::DrawGuard& draw,
                  IDirect3DPixelShader9* maskShader, IDirect3DPixelShader9* blurShader) {
        device->SetRenderTarget(0, g_mask.First().surface);
        device->SetPixelShader(maskShader);
        draw.ClipQuad(0.0f);
        device->SetPixelShader(blurShader);
        BlurFrom(device, draw, g_mask);
    }

    // The far world from `world`, at quarter resolution, blurred.
    void DrawFar(IDirect3DDevice9* device, SkyOverhaul::DrawGuard& draw, IDirect3DTexture9* world,
                 IDirect3DPixelShader9* downShader, IDirect3DPixelShader9* blurShader) {
        const float pixel[4] = {0.0f, 0.0f, 1.0f / static_cast<float>(g_blurred.Width()),
                                1.0f / static_cast<float>(g_blurred.Height())};
        device->SetPixelShaderConstantF(kAxisConstant, pixel, 1);
        device->SetTexture(0, world);
        device->SetRenderTarget(0, g_blurred.First().surface);
        device->SetPixelShader(downShader);
        draw.ClipQuad(0.0f);
        device->SetPixelShader(blurShader);
        BlurFrom(device, draw, g_blurred);
    }

    void Filter(IDirect3DDevice9* device, DWORD sampler) {
        device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
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
    const float farBlur = heat * v.hazeFarBlur;
    if ((std::max)({heat * v.hazeStrength, mirage, farBlur}) < kFaintest) {
        return;
    }
    IDirect3DDevice9* device = pass.device;
    IDirect3DPixelShader9* shader = g_shader.Get(device);
    IDirect3DTexture9* world =
        shader != nullptr && Noise::Ensure(device) ? FrameCopy::Take(pass) : nullptr;
    if (world == nullptr) {
        return;
    }

    ScreenDraw draw(device, kFirstConstant, kAxisConstant + 1 - kFirstConstant);
    DepthConstants::Set(device, view, depth, pass.backBuffer.Width, pass.backBuffer.Height);
    float projection[2];
    SolidDepth::Projection(view, projection);
    const float slope = v.mirageSlope * kRadiansPerDegree;
    const UINT halfWidth = (pass.backBuffer.Width + 1) / 2;
    const UINT halfHeight = (pass.backBuffer.Height + 1) / 2;
    // A scope magnifies the air's bend less than the world, so the heat stays shimmer.
    g_widest = g_widest > 0.0f ? (std::min)(g_widest, view.verticalScale) : view.verticalScale;
    const float zoom = view.verticalScale / g_widest;
    float constants[kOwnConstants * 4] = {
        heat * v.hazeStrength * kFullBend / std::sqrt(zoom), mirage,
        kStandingEye / v.mirageDistance, 0.0f,
        g_seconds, zoom, v.hazeWaveWidth * kRadiansPerArcMinute * kWaveReference,
        v.hazeWaveHeight * kRadiansPerArcMinute * kWaveReference,
        projection[0], projection[1], 1.0f / static_cast<float>(halfWidth),
        1.0f / static_cast<float>(halfHeight),
        std::cos(slope), std::cos(0.5f * slope), v.mirageDistance, 2.0f * v.mirageDistance,
        v.hazeBlurStart, (std::max)(v.hazeBlurEnd, v.hazeBlurStart + 1.0f), farBlur,
        v.hazeFarWobble};
    device->SetPixelShaderConstantF(kOwnConstant, constants, kOwnConstants);
    device->SetTexture(kSolidDepthSampler, solid.texture);

    IDirect3DPixelShader9* maskShader = g_maskShader.Get(device);
    IDirect3DPixelShader9* downShader = g_downShader.Get(device);
    IDirect3DPixelShader9* blurShader = g_blurShader.Get(device);
    const bool masked = mirage >= kFaintest && maskShader != nullptr && blurShader != nullptr &&
                        g_mask.Ensure(device, halfWidth, halfHeight);
    const bool blurred = farBlur >= kFaintest && downShader != nullptr && blurShader != nullptr &&
                         g_blurred.Ensure(device, (pass.backBuffer.Width + 3) / 4,
                                          (pass.backBuffer.Height + 3) / 4,
                                          D3DFMT_A16B16G16R16F);
    if (masked || blurred) {
        // A smaller target cannot share the world's multisampled depth, and switching targets
        // resets the scissor rectangle the engine may be counting on.
        RECT scissor = {};
        device->GetScissorRect(&scissor);
        device->SetDepthStencilSurface(nullptr);
        if (masked) {
            DrawMask(device, draw, maskShader, blurShader);
        }
        if (blurred) {
            Filter(device, 0);
            DrawFar(device, draw, world, downShader, blurShader);
        }
        device->SetRenderTarget(0, pass.target);
        device->SetDepthStencilSurface(pass.depth);
        device->SetScissorRect(&scissor);
        D3DVIEWPORT9 wholeRange = pass.viewport;
        wholeRange.MinZ = 0.0f;
        wholeRange.MaxZ = 1.0f;
        device->SetViewport(&wholeRange);
    }
    constants[kMirageStrength] = masked ? mirage : 0.0f;
    constants[kFarStrength] = blurred ? farBlur : 0.0f;
    device->SetPixelShaderConstantF(kOwnConstant, constants, kOwnConstants);

    // The world target's alpha carries brightness for the bloom.
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShader(shader);
    device->SetTexture(0, world);
    device->SetTexture(kMaskSampler, masked ? g_mask.First().texture : nullptr);
    device->SetTexture(kBlurredSampler, blurred ? g_blurred.First().texture : nullptr);
    device->SetTexture(kTurbulenceSampler, Noise::Turbulence());
    // The bend moves a look by fractions of a pixel, the mask and the blur are smaller, and the
    // turbulence is a few texels a cell.
    for (DWORD sampler : {DWORD{0}, kMaskSampler, kBlurredSampler, kTurbulenceSampler}) {
        Filter(device, sampler);
    }
    for (D3DSAMPLERSTATETYPE axis : {D3DSAMP_ADDRESSU, D3DSAMP_ADDRESSV, D3DSAMP_ADDRESSW}) {
        device->SetSamplerState(kTurbulenceSampler, axis, D3DTADDRESS_WRAP);
    }
    draw.ClipQuad(0.0f);
}

void SkyOverhaul::Haze::ReleaseDeviceObjects() {
    g_mask.Release();
    g_blurred.Release();
    g_shader.Release();
    g_maskShader.Release();
    g_downShader.Release();
    g_blurShader.Release();
}

void SkyOverhaul::Haze::SetEnabled(bool enabled) {
    g_enabled = enabled;
    SolidDepth::SetWanted(SolidDepth::User::Haze, enabled);
}
