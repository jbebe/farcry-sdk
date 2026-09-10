#include "clouds.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/log.h"
#include "engine/noise.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"

#include "clouds_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 17;

    // The far end of the depth range, where nothing but sky has been drawn: the world's geometry
    // is all nearer, so a less-or-equal test rejects the quad wherever anything stands, at any
    // distance. A value short of one would stop occluding somewhere down the view distance.
    constexpr float kSkyDepth = 1.0f;

    // How far out clouds are drawn, over how much of the last of that they fade away, and how far
    // the march itself runs. A layer is a plane, so a ray near the horizon would otherwise run for
    // ever, and the samples would be spread so thin they stepped past whole clouds.
    constexpr float kMaxDistance = 30000.0f;
    constexpr float kFadeDistance = 22000.0f;
    constexpr float kMarchDistance = 8000.0f;

    // How fast the layer drifts at a wind of one, in metres a second. The engine's own wind offset
    // advances too slowly to read as weather, so only its direction is taken from there.
    constexpr float kWindSpeed = 9.0f;

    // How far apart the samples toward the light are. Wide enough that five of them reach through a
    // whole cloud, which is what a shadow inside one needs.
    constexpr float kLightStride = 90.0f;

    // The sun's height, as a sine, below which no layer can see it and the moon lights the clouds.
    constexpr float kSunGone = -0.07f;
    // How much further the sun sinks, as a sine, while the moonlight comes up to full.
    constexpr float kMoonRising = 0.13f;

    // Moonlight at full strength, and the share of it a thin edge lets through.
    constexpr float kMoonColour[3] = {1.6f, 1.8f, 2.0f};
    constexpr float kMoonBackShare = 0.13f;
    // How far below the horizon, as a sine, the moon's glow takes to fade out.
    constexpr float kMoonBelow = 0.1f;

    // The high sheet: how far above the layer it sits at least, how many metres one repeat of its
    // streaks covers, and how hard those streaks are squashed across the wind.
    constexpr float kCirrusClearance = 2000.0f;
    constexpr float kCirrusFloor = 6000.0f;
    constexpr float kCirrusGrain = 1.0f / 20000.0f;

    // How wide a fresh trail is, and how fast the field breaking it up runs along its length. The
    // second is what decides how many gaps there are across a sky: too slow and the field barely
    // moves over the whole visible trail, which leaves one unbroken line.
    constexpr float kTrailWidth = 0.011f;
    constexpr float kTrailBreak = 0.9f;

    // How much light bends forward off a droplet, and the two frequencies the detail and the
    // weather are read at relative to the shape.
    constexpr float kForwardScatter = 0.55f;
    constexpr float kDetailRepeats = 11.0f;
    constexpr float kWeatherRepeats = 0.18f;

    // Written by the settings callbacks and read while drawing.
    bool g_enabled = false;
    float g_baseAltitude = 1200.0f;
    float g_thickness = 700.0f;
    float g_coverage = 0.45f;
    float g_density = 0.04f;
    float g_detail = 0.35f;
    float g_grain = 4000.0f;
    float g_wind = 1.0f;
    float g_haze = 3000.0f;
    float g_cirrus = 0.35f;
    float g_cirrusOpacity = 0.7f;
    float g_contrails = 0.6f;
    float g_moonlight = 1.0f;
    float g_moonGlow = 0.25f;

    // Which frame was last drawn into. A frame can hold more than one pass the sky is drawn in,
    // and drawing into each of them would blend the clouds over themselves.
    uint32_t g_drawnFrame = 0;

    // How far the layer has drifted, in metres, kept here rather than derived from the wind so
    // that changing the wind changes how fast the clouds move and not where they are.
    float g_drift[2] = {0.0f, 0.0f};

    SkyOverhaul::PixelShader g_shader{"clouds", g_cloudsPixelShader};
    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{2.0f};

    // The one light the shader marches toward, and the glow of the air around the moon.
    struct Light {
        float direction[3];
        float colour[3];
        float back[3];
        float glow[3];
    };

    // The sun until it has set for every layer, then the moon, coming up as the sun sinks further.
    Light ChooseLight(const SkyOverhaul::CloudLayer::Lighting& lighting) {
        const bool sun = lighting.sunDirection[2] > kSunGone;
        const float rising = (kSunGone - lighting.sunDirection[2]) / kMoonRising;
        const float share = sun ? 0.0f : (rising < 1.0f ? rising : 1.0f);
        const float up = (lighting.moonDirection[2] + kMoonBelow) / kMoonBelow;
        const float glow = share * std::clamp(up, 0.0f, 1.0f) * g_moonGlow;

        Light light;
        for (int c = 0; c < 3; c++) {
            light.direction[c] = sun ? lighting.sunDirection[c] : lighting.moonDirection[c];
            light.colour[c] = sun ? lighting.sunColour[c] : kMoonColour[c] * share * g_moonlight;
            light.back[c] = sun ? lighting.backSunColour[c] : light.colour[c] * kMoonBackShare;
            light.glow[c] = kMoonColour[c] * glow;
        }
        return light;
    }

    void LogPass(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
                 float elapsed) {
        SkyOverhaul::Logf("clouds f%u: eye (%.1f %.1f %.1f) base %.0f | dir (%.2f %.2f %.2f) "
                          "bloom %.2f | %.2f ms",
                          pass.frame, view.eye[0], view.eye[1], view.eye[2], g_baseAltitude,
                          view.direction[0], view.direction[1], view.direction[2], view.bloom,
                          elapsed * 1000.0f);
    }

    // Carries the layer along on the plugin's own clock, in the direction the engine is blowing.
    // The offsets are wrapped by the shape's own repeat, which the noise tiles at, so a long
    // session cannot drift far enough for the arithmetic to coarsen.
    void Advance(const SkyOverhaul::CloudLayer::Lighting& lighting, float elapsed) {
        float x = lighting.wind[0];
        float y = lighting.wind[1];
        const float length = std::sqrt(x * x + y * y);
        if (length > 0.0001f) {
            x /= length;
            y /= length;
        } else {
            x = 1.0f;
            y = 0.0f;
        }

        const float step = kWindSpeed * g_wind * elapsed;
        g_drift[0] = std::fmod(g_drift[0] + x * step, g_grain);
        g_drift[1] = std::fmod(g_drift[1] + y * step, g_grain);
    }

    void Draw(const SkyOverhaul::Frame::Pass& pass, IDirect3DPixelShader9* shader,
              const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::CloudLayer::Lighting& lighting) {
        // Kept clear of the layer below it however high that is set, so the two never interleave.
        const float above = g_baseAltitude + g_thickness + kCirrusClearance;
        const float cirrusAltitude = above > kCirrusFloor ? above : kCirrusFloor;

        const Light light = ChooseLight(lighting);
        const float shapeGrain = 1.0f / g_grain;
        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            g_baseAltitude, g_thickness, g_coverage, g_density,
            g_drift[0], g_drift[1], g_drift[0] * 0.5f, g_drift[1] * 0.5f,
            shapeGrain, shapeGrain * kDetailRepeats, shapeGrain * kWeatherRepeats, g_detail,
            light.direction[0], light.direction[1], light.direction[2], kForwardScatter,
            light.colour[0], light.colour[1], light.colour[2], kLightStride,
            lighting.ambientColour[0], lighting.ambientColour[1], lighting.ambientColour[2], 0.0f,
            light.back[0], light.back[1], light.back[2], 0.0f,
            kMaxDistance, kFadeDistance, kMarchDistance, g_haze,
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogValues[0], view.fogValues[1], view.fogValues[2], 0.0f,
            view.fogHeightValues[0], view.fogHeightValues[1], view.fogHeightValues[2],
            view.fogHeightValues[3],
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f,
            cirrusAltitude, kCirrusGrain, g_cirrus, g_cirrusOpacity,
            g_contrails, kTrailWidth, kTrailBreak, 0.0f,
            light.glow[0], light.glow[1], light.glow[2], 0.0f};

        SkyOverhaul::ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
        pass.device->SetPixelShader(shader);
        pass.device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);

        pass.device->SetTexture(0, SkyOverhaul::Noise::Shape());
        pass.device->SetTexture(1, SkyOverhaul::Noise::Detail());
        pass.device->SetTexture(2, SkyOverhaul::Noise::Weather());
        for (DWORD sampler = 0; sampler < 3; sampler++) {
            pass.device->SetSamplerState(sampler, D3DSAMP_ADDRESSU, D3DTADDRESS_WRAP);
            pass.device->SetSamplerState(sampler, D3DSAMP_ADDRESSV, D3DTADDRESS_WRAP);
            pass.device->SetSamplerState(sampler, D3DSAMP_ADDRESSW, D3DTADDRESS_WRAP);
            pass.device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
            pass.device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        }

        // Tested against the world's own depth, which this pass still owns, and blended the way
        // the engine's cloud layer blended: colour already multiplied in, alpha what survives.
        pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        pass.device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        pass.device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
        pass.device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCALPHA);

        draw.ClipQuad(kSkyDepth, view.corners);
    }
}

void SkyOverhaul::Clouds::Install() {
    Noise::Start();
}

void SkyOverhaul::Clouds::OnScenePass(const Frame::Pass& pass) {
    // Every scene pass arrives here and only one of them is the sky, so the clock is read after
    // the test rather than before it: ticking on all of them would leave the heartbeat measuring
    // the gap between two passes instead of the time between two frames.
    if (!g_enabled || !pass.sky || !pass.live || pass.frame == g_drawnFrame) {
        return;
    }
    g_drawnFrame = pass.frame;
    const float elapsed = g_clock.Lap();

    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    Camera::View view;
    CloudLayer::Lighting lighting;
    if (shader == nullptr || !Camera::Read(pass.device, view) || !CloudLayer::Latest(lighting) ||
        !Noise::Ensure(pass.device)) {
        return;
    }

    Advance(lighting, elapsed);
    Draw(pass, shader, view, lighting);

    if (g_heartbeat.Due(elapsed)) {
        LogPass(pass, view, elapsed);
    }
}

void SkyOverhaul::Clouds::ReleaseDeviceObjects() {
    Noise::ReleaseDeviceObjects();
    g_shader.Release();
}

void SkyOverhaul::Clouds::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void SkyOverhaul::Clouds::SetBaseAltitude(int metres) {
    g_baseAltitude = static_cast<float>(metres);
}

void SkyOverhaul::Clouds::SetThickness(int metres) {
    g_thickness = static_cast<float>(metres);
}

void SkyOverhaul::Clouds::SetCoverage(int percent) {
    g_coverage = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetDensity(int percent) {
    g_density = static_cast<float>(percent) / 1000.0f;
}

void SkyOverhaul::Clouds::SetDetail(int percent) {
    g_detail = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetGrain(int metres) {
    g_grain = static_cast<float>(metres);
}

void SkyOverhaul::Clouds::SetWind(int percent) {
    g_wind = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetHaze(int metres) {
    g_haze = static_cast<float>(metres);
}

void SkyOverhaul::Clouds::SetCirrus(int percent) {
    g_cirrus = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetCirrusOpacity(int percent) {
    g_cirrusOpacity = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetContrails(int percent) {
    g_contrails = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetMoonlight(int percent) {
    g_moonlight = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Clouds::SetMoonGlow(int percent) {
    g_moonGlow = static_cast<float>(percent) / 100.0f;
}
