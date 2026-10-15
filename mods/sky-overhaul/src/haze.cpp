#include "haze.h"

#include "depth_constants.h"
#include "sky_model.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/depth_texture.h"
#include "engine/frame_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"

#include "haze_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 2;

    // The sun's height, as a sine, over which the ground heats up: about 15 to 45 degrees.
    constexpr float kHeatLow = 0.26f;
    constexpr float kHeatHigh = 0.7f;

    // How far the air bends a look at full strength, in radians: about two pixels at 1080 lines.
    constexpr float kFullBend = 0.002f;

    // The sine of the steepest look onto flat ground that still shows a mirage: about 1.2 degrees.
    constexpr float kMirageSine = 0.021f;

    // How deep the hot air over the ground is, in metres.
    constexpr float kHotLayer = 2.0f;

    // Kept short so the shimmer's coordinates stay precise; it jumps once when the clock wraps.
    constexpr float kClockWrap = 3600.0f;

    // Below this neither changes anything a player could see.
    constexpr float kFaintest = 0.002f;

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"haze", g_hazePixelShader};
    SkyOverhaul::Stopwatch g_stopwatch;
    float g_seconds = 0.0f;
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
    if (heat * (std::max)(v.hazeStrength, v.mirageStrength) < kFaintest) {
        return;
    }
    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    IDirect3DTexture9* world = shader != nullptr ? FrameCopy::Take(pass) : nullptr;
    if (world == nullptr) {
        return;
    }

    ScreenDraw draw(pass.device, kFirstConstant, kOwnConstant + kOwnConstants - kFirstConstant);
    DepthConstants::Set(pass.device, view, depth, pass.backBuffer.Width, pass.backBuffer.Height);
    const float constants[kOwnConstants * 4] = {
        heat * v.hazeStrength * kFullBend, heat * v.mirageStrength, kMirageSine, kHotLayer,
        g_seconds, 0.0f, 0.0f, 0.0f};
    pass.device->SetPixelShaderConstantF(kOwnConstant, constants, kOwnConstants);

    // The world target's alpha carries brightness for the bloom.
    pass.device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                            D3DCOLORWRITEENABLE_GREEN |
                                                            D3DCOLORWRITEENABLE_BLUE);
    pass.device->SetPixelShader(shader);
    pass.device->SetTexture(0, world);
    // The bend moves a look by fractions of a pixel.
    pass.device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    pass.device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    draw.ClipQuad(0.0f);
}

void SkyOverhaul::Haze::ReleaseDeviceObjects() {
    g_shader.Release();
}

void SkyOverhaul::Haze::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
