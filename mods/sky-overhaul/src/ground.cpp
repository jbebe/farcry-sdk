#include "ground.h"

#include "depth_constants.h"
#include "wet.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/depth_texture.h"
#include "engine/frame_copy.h"
#include "engine/noise.h"
#include "engine/rain_cover.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/solid_depth.h"

#include "ground_ps.h"

#include <cmath>

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 7;
    constexpr UINT kCoverConstant = kOwnConstant + kOwnConstants;
    constexpr UINT kUpRainConstant = kCoverConstant + 4;

    constexpr DWORD kSceneSampler = 0;
    constexpr DWORD kSolidDepthSampler = 1;
    constexpr DWORD kCoverSampler = 2;
    constexpr DWORD kShapeSampler = 4;

    // How far in front of the solid depth grass and leaves stand before they count as such, and
    // how far out the wet reaches, in metres.
    constexpr float kFoliageDepth = 0.15f;
    constexpr float kReach = 150.0f;
    // How far open ground darkens toward mud, nought to one.
    constexpr float kMud = 1.0f;
    // The sky overhead against the sky low down, and how many times the sky's brightness a pixel
    // has to be to keep its light.
    constexpr float kOverhead = 1.15f;
    constexpr float kLit = 2.0f;
    // The patterns repeat every this many metres, and the rain's clock wraps every this many seconds.
    constexpr double kRepeat = 256.0;
    constexpr double kClockWrap = 4096.0;

    constexpr float kDriest = 0.003f;

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"ground", g_groundPixelShader};
    SkyOverhaul::Stopwatch g_stopwatch;
    double g_seconds = 0.0;

    float Wrap(double value, double period) {
        return static_cast<float>(value - std::floor(value / period) * period);
    }

    void Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& depth,
              const SkyOverhaul::SolidDepth::Found& solid, float wetness, float rain,
              float puddles) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* shader = g_shader.Get(device);
        if (shader == nullptr || !SkyOverhaul::Noise::Ensure(device)) {
            return;
        }
        IDirect3DTexture9* scene = SkyOverhaul::FrameCopy::Take(pass);
        if (scene == nullptr) {
            return;
        }

        float projection[2];
        SkyOverhaul::SolidDepth::Projection(view, projection);
        const float* fog = view.fogColour;
        const float brightness = fog[0] * 0.299f + fog[1] * 0.587f + fog[2] * 0.114f;
        const float own[kOwnConstants * 4] = {
            projection[0], projection[1], kFoliageDepth, kReach,
            wetness, rain, puddles, Wrap(g_seconds, kClockWrap),
            view.fogValues[0], view.fogValues[1], view.fogValues[2], view.eye[2],
            view.fogHeightValues[0], view.fogHeightValues[1], view.fogHeightValues[2],
            view.fogHeightValues[3],
            fog[0], fog[1], fog[2], kMud,
            fog[0] * kOverhead, fog[1] * kOverhead, fog[2] * kOverhead, brightness * kLit,
            Wrap(view.eye[0], kRepeat), Wrap(view.eye[1], kRepeat),
            1.0f / static_cast<float>(solid.width), 1.0f / static_cast<float>(solid.height)};
        float cover[16];
        float upRain[4];
        SkyOverhaul::RainCover::ForShaders(view, cover, upRain);

        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kUpRainConstant + 1 - kFirstConstant);
        SkyOverhaul::DepthConstants::Set(device, view, depth, pass.backBuffer.Width,
                                         pass.backBuffer.Height);
        device->SetPixelShaderConstantF(kOwnConstant, own, kOwnConstants);
        device->SetPixelShaderConstantF(kCoverConstant, cover, 4);
        device->SetPixelShaderConstantF(kUpRainConstant, upRain, 1);

        SkyOverhaul::RainCover::Found found;
        device->SetTexture(kSceneSampler, scene);
        device->SetTexture(kSolidDepthSampler, solid.texture);
        device->SetTexture(kCoverSampler,
                           SkyOverhaul::RainCover::LastSeen(found) ? found.texture : nullptr);
        device->SetTexture(kShapeSampler, SkyOverhaul::Noise::Shape());
        for (D3DSAMPLERSTATETYPE axis : {D3DSAMP_ADDRESSU, D3DSAMP_ADDRESSV, D3DSAMP_ADDRESSW}) {
            device->SetSamplerState(kShapeSampler, axis, D3DTADDRESS_WRAP);
        }
        // Filtered, a shadow texture's projected read is a soft compare.
        for (DWORD sampler : {kCoverSampler, kShapeSampler}) {
            device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
            device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        }
        // The world target's alpha carries brightness for the bloom.
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetPixelShader(shader);
        draw.ClipQuad(0.0f, view.corners);
    }
}

void SkyOverhaul::Ground::SetEnabled(bool enabled) {
    g_enabled = enabled;
    SolidDepth::SetWanted(SolidDepth::User::Ground, enabled);
}

void SkyOverhaul::Ground::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const float seconds = g_stopwatch.Lap();
    const float wetness = Wet::Wetness();
    const float rain = Wet::Rain();
    g_seconds += seconds;
    if (wetness < kDriest) {
        return;
    }
    Camera::View view;
    DepthTexture::Found depth = {};
    SolidDepth::Found solid = {};
    if (Camera::Read(pass.device, view) && view.verticalScale != 0.0f &&
        DepthTexture::Latest(depth) && SolidDepth::Latest(solid)) {
        Draw(pass, view, depth, solid, wetness, rain, Wet::Puddles());
    }
}

void SkyOverhaul::Ground::ReleaseDeviceObjects() {
    g_shader.Release();
}
