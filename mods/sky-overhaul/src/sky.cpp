#include "sky.h"

#include "angles.h"
#include "sky_model.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/dome_draw.h"
#include "engine/fog_tint.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/time_of_day.h"
#include "tuning.h"

#include "sky_land_ps.h"
#include "sky_ps.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <utility>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them. The clouds use the same range: the two never draw in
    // one call, and each puts back what it found.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 8;


    // The most the zenith may be lifted, and the height of the sun over which that lift is let go,
    // as sines of its elevation: held in full above twenty-five degrees, gone by five, so that
    // sunset still darkens the sky overhead the way it should.
    constexpr float kZenithHoldMax = 3.0f;
    constexpr float kZenithHoldLow = 0.087f;
    constexpr float kZenithHoldHigh = 0.423f;

    // What the sun is worth in the shader, and what a clear day's high sun is cut to so the sky no
    // longer clips.
    constexpr float kSunIntensity = 41.4f;
    constexpr float kDaylight = 0.5f;

    // The sun's height, as a sine, over which the land's fog darkens for the dusk: from seven degrees
    // up to a degree and a half.
    constexpr float kDuskStart = 0.122f;
    constexpr float kDuskEnd = 0.026f;

    // How bright the filled land is drawn at each hour of the game's clock, against the fog's own.
    constexpr float kLandByHour[24] = {1.0f,  1.0f,  1.0f,  1.0f,  1.0f,  0.48f, 0.42f, 1.26f,
                                       1.21f, 1.01f, 1.06f, 1.03f, 1.2f,  1.24f, 1.23f, 1.16f,
                                       1.17f, 1.24f, 1.04f, 1.05f, 0.89f, 1.0f,  0.44f, 0.42f};

    SkyOverhaul::PixelShader g_shader{"sky", g_skyPixelShader};
    SkyOverhaul::PixelShader g_landShader{"sky land", g_skyLandPixelShader};

    bool g_enabled = false;
    // Whether our sky drew filled land since the last pass ended, and what it was drawn with.
    bool g_landDrawn = false;
    std::array<float, kConstantCount * 4> g_constants;
    float g_corners[4][3];

    // The filled land's brightness at `hour` of the clock, blended between the hours either side.
    float LandBrightness(float hour) {
        const int before = static_cast<int>(hour) % 24;
        const float after = kLandByHour[(before + 1) % 24];
        return kLandByHour[before] + (after - kLandByHour[before]) * (hour - std::floor(hour));
    }

    float Luminance(const float colour[3]) {
        return colour[0] * 0.299f + colour[1] * 0.587f + colour[2] * 0.114f;
    }

    float SmoothStep(float from, float to, float value) {
        const float t = std::clamp((value - from) / (to - from), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    // How many times brighter the zenith has to be drawn to look as it would under an overhead sun,
    // measured by the model on clean air. Haze is left out on purpose: with the sun overhead its
    // glow sits right on the zenith, and a reference that includes it would ask for the aureole's
    // brightness all afternoon rather than the sky's.
    float ZenithLift(const float sun[3], float eyeHeight, float intensity) {
        const float up[3] = {0.0f, 0.0f, 1.0f};
        float overhead[3];
        float now[3];
        SkyOverhaul::SkyModel::Radiance(up, up, eyeHeight, 0.0f, intensity, overhead);
        SkyOverhaul::SkyModel::Radiance(up, sun, eyeHeight, 0.0f, intensity, now);
        const float nowLuminance = Luminance(now);
        const float lift =
            nowLuminance > 1.0e-6f
                ? std::clamp(Luminance(overhead) / nowLuminance, 1.0f, kZenithHoldMax)
                : kZenithHoldMax;
        const float kept = SmoothStep(kZenithHoldLow, kZenithHoldHigh, sun[2]);
        return 1.0f + (lift - 1.0f) * kept;
    }

    // Runs from inside the dome's own draw call, with everything the engine set for it still bound.
    // Nothing here may change a render state, a sampler or a texture: the sun, the moon and the
    // stars are drawn after this with no blend or depth mode of their own, and inherit whatever the
    // dome left behind.
    bool Draw(IDirect3DDevice9* device) {
        IDirect3DPixelShader9* shader = g_shader.Get(device);
        SkyOverhaul::Camera::View view;
        SkyOverhaul::CloudLayer::Lighting lighting;
        if (shader == nullptr || !SkyOverhaul::Camera::Read(device, view) ||
            !SkyOverhaul::CloudLayer::Latest(lighting)) {
            return false;
        }

        // The weather is folded in here, so what crosses into the shader is already finished.
        const float storminess = SkyOverhaul::SkyModel::Storminess(lighting.storm);
        const float haze = SkyOverhaul::SkyModel::Haze(storminess);
        const float intensity =
            kSunIntensity * SkyOverhaul::SkyModel::SunShare(storminess) *
            SkyOverhaul::SkyModel::DaylightCut(lighting.sunDirection[2], storminess, kDaylight);

        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        float hour = 12.0f;
        SkyOverhaul::TimeOfDay::Hour(hour);
        SkyOverhaul::FogTint::SetDusk(SmoothStep(kDuskStart, kDuskEnd, lighting.sunDirection[2]),
                                      v.duskFogBrightness, lighting.sunDirection);

        float fill[2];
        SkyOverhaul::Sky::Fill(v, fill);
        g_constants = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            lighting.sunDirection[0], lighting.sunDirection[1], lighting.sunDirection[2],
            lighting.night,
            haze, intensity, ZenithLift(lighting.sunDirection, view.eye[2], intensity),
            SkyOverhaul::SkyModel::Grey(storminess),
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f,
            fill[0], fill[1], v.horizonFillHaze, LandBrightness(hour),
            lighting.moonDirection[0], lighting.moonDirection[1], lighting.moonDirection[2], 0.0f};
        std::copy_n(&view.corners[0][0], 12, &g_corners[0][0]);

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kConstantCount);
        device->SetPixelShader(shader);
        device->SetPixelShaderConstantF(kFirstConstant, g_constants.data(), kConstantCount);
        const bool drawn = guard.ClipQuad(SkyOverhaul::Frame::kFarDepth, view.corners);
        g_landDrawn = drawn && fill[0] > 0.0f;
        return drawn;
    }
}

void SkyOverhaul::Sky::Install() {
    DomeDraw::Install(&Draw);
}

void SkyOverhaul::Sky::OnScenePass(const Frame::Pass& pass) {
    if (!std::exchange(g_landDrawn, false) || !pass.sky || !pass.live) {
        return;
    }
    IDirect3DPixelShader9* shader = g_landShader.Get(pass.device);
    if (shader == nullptr) {
        return;
    }
    // Tested against the world's depth like the sky, and blended by how much land covers a pixel.
    ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
    pass.device->SetPixelShader(shader);
    pass.device->SetPixelShaderConstantF(kFirstConstant, g_constants.data(), kConstantCount);
    pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
    pass.device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    pass.device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
    pass.device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    draw.ClipQuad(Frame::kFarDepth, g_corners);
}

void SkyOverhaul::Sky::Fill(const Tuning::Values& values, float fill[2]) {
    fill[0] = g_enabled ? values.horizonFill : 0.0f;
    fill[1] = std::sin(Radians(values.horizonFillHeight));
}

void SkyOverhaul::Sky::ReleaseDeviceObjects() {
    g_shader.Release();
    g_landShader.Release();
}

void SkyOverhaul::Sky::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetMode(enabled ? DomeDraw::Mode::Overhaul : DomeDraw::Mode::Engine);
    // With the engine drawing its own sky there is nothing for the world's fog to agree with, so it
    // goes back to the colour the engine chose.
    if (!enabled) {
        FogTint::Forget();
    }
}
