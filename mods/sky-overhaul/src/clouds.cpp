#include "clouds.h"

#include "sky_model.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/dome_draw.h"
#include "engine/noise.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "clouds_cover_ps.h"
#include "clouds_mask_ps.h"
#include "clouds_ps.h"
#include "clouds_shadow_ps.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 17;

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

    // How bright sunlight is on the clouds where our air lets all of it through.
    constexpr float kSunlight = 6.6f;

    // Moonlight at full strength, the share of either light a thin edge lets through, and how
    // brightly the air glows around the moon.
    constexpr float kMoonColour[3] = {1.6f, 1.8f, 2.0f};
    constexpr float kBackShare = 0.13f;
    constexpr float kMoonGlow = 0.25f;

    // What a full storm makes of the layer's coverage and water and of the high sheet's opacity, and
    // how much of the sky's light it takes from the base of its water-laden cloud.
    constexpr float kStormCoverage = 0.65f;
    constexpr float kStormDensity = 0.09f;
    constexpr float kStormCirrusOpacity = 0.8f;
    constexpr float kStormBaseShade = 0.8f;

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

    using SkyOverhaul::Tuning::Values;

    bool g_enabled = false;

    // How far the layer has drifted, in metres, kept here rather than derived from the wind so
    // that changing the wind changes how fast the clouds move and not where they are.
    float g_drift[2] = {0.0f, 0.0f};

    // What the clouds were last drawn with, which the sun's cover is drawn through again.
    float g_constants[kConstantCount * 4] = {};
    float g_corners[4][3] = {};
    bool g_drawn = false;

    SkyOverhaul::PixelShader g_shader{"clouds", g_cloudsPixelShader};
    SkyOverhaul::PixelShader g_coverShader{"clouds cover", g_cloudsCoverPixelShader};
    SkyOverhaul::PixelShader g_maskShader{"clouds mask", g_cloudsMaskPixelShader};
    SkyOverhaul::PixelShader g_shadowShader{"clouds shadow", g_cloudsShadowPixelShader};
    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{2.0f};

    // The one light the shader marches toward, and the glow of the air around the moon.
    struct Light {
        float direction[3];
        float colour[3];
        float back[3];
        float glow[3];
    };

    // The day's values carried toward an overcast sky as the storm rises: more of the sky filled,
    // more water, the high sheet across all of it, and no aircraft above.
    Values Weathered(Values v, float storminess) {
        v.cloudCoverage += (kStormCoverage - v.cloudCoverage) * storminess;
        v.cloudDensity += (kStormDensity - v.cloudDensity) * storminess;
        v.cirrus += (1.0f - v.cirrus) * storminess;
        v.cirrusOpacity += (kStormCirrusOpacity - v.cirrusOpacity) * storminess;
        v.contrails -= v.contrails * storminess;
        return v;
    }

    // The sun until it has set for every layer, then the moon, coming up as the sun sinks further
    // and as the moon itself climbs. The sun's light is what our own air lets through to the middle
    // of the layer, dimmed as the sky is in a storm.
    Light ChooseLight(const SkyOverhaul::CloudLayer::Lighting& lighting, const Values& v,
                      float storminess) {
        const bool sun = lighting.sunDirection[2] > kSunGone;
        const float rising = (kSunGone - lighting.sunDirection[2]) / kMoonRising;
        const float share = sun ? 0.0f : (rising < 1.0f ? rising : 1.0f);
        const float moon = share * SkyOverhaul::SkyModel::MoonRise(lighting.moonDirection[2]);

        float sunlight[3];
        SkyOverhaul::SkyModel::Sunlight(lighting.sunDirection,
                                        v.cloudBase + 0.5f * v.cloudThickness,
                                        SkyOverhaul::SkyModel::Haze(storminess), sunlight);
        const float sunBrightness = kSunlight * SkyOverhaul::SkyModel::SunShare(storminess);

        Light light;
        for (int c = 0; c < 3; c++) {
            light.direction[c] = sun ? lighting.sunDirection[c] : lighting.moonDirection[c];
            light.colour[c] = sun ? sunlight[c] * sunBrightness : kMoonColour[c] * moon;
            light.back[c] = light.colour[c] * kBackShare;
            light.glow[c] = kMoonColour[c] * moon * kMoonGlow;
        }
        return light;
    }

    void LogPass(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
                 const Light& light, const Values& v, float storm, float elapsed) {
        FCSE::Logf("clouds f%u: eye (%.1f %.1f %.1f) base %.0f | dir (%.2f %.2f %.2f) "
                   "bloom %.2f | %.2f ms",
                   pass.frame, view.eye[0], view.eye[1], view.eye[2], v.cloudBase,
                   view.direction[0], view.direction[1], view.direction[2], view.bloom,
                   elapsed * 1000.0f);
        FCSE::Logf("clouds f%u: storm %.2f coverage %.2f density %.3f cirrus %.2f opacity %.2f "
                   "| light (%.3f %.3f %.3f)",
                   pass.frame, storm, v.cloudCoverage, v.cloudDensity, v.cirrus, v.cirrusOpacity,
                   light.colour[0], light.colour[1], light.colour[2]);
    }

    // Carries the layer along on the plugin's own clock, in the direction the engine is blowing.
    // The offsets are wrapped by the shape's own repeat, which the noise tiles at, so a long
    // session cannot drift far enough for the arithmetic to coarsen.
    void Advance(const SkyOverhaul::CloudLayer::Lighting& lighting, const Values& v,
                 float elapsed) {
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

        const float step = kWindSpeed * v.cloudWind * elapsed;
        g_drift[0] = std::fmod(g_drift[0] + x * step, v.cloudSize);
        g_drift[1] = std::fmod(g_drift[1] + y * step, v.cloudSize);
    }

    // The shader, this frame's constants and the noise, sampled the way the shape expects.
    void Bind(IDirect3DDevice9* device, IDirect3DPixelShader9* shader) {
        device->SetPixelShader(shader);
        device->SetPixelShaderConstantF(kFirstConstant, g_constants, kConstantCount);

        device->SetTexture(0, SkyOverhaul::Noise::Shape());
        device->SetTexture(1, SkyOverhaul::Noise::Detail());
        device->SetTexture(2, SkyOverhaul::Noise::Weather());
        for (DWORD sampler = 0; sampler < 3; sampler++) {
            device->SetSamplerState(sampler, D3DSAMP_ADDRESSU, D3DTADDRESS_WRAP);
            device->SetSamplerState(sampler, D3DSAMP_ADDRESSV, D3DTADDRESS_WRAP);
            device->SetSamplerState(sampler, D3DSAMP_ADDRESSW, D3DTADDRESS_WRAP);
            device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
            device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        }
    }

    // A quad over the sky through the shader, tested against the world's own depth, which the pass
    // still owns, and blended source over destination.
    bool DrawSky(IDirect3DDevice9* device, IDirect3DPixelShader9* shader, D3DBLEND source,
                 D3DBLEND destination) {
        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kConstantCount);
        Bind(device, shader);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, source);
        device->SetRenderState(D3DRS_DESTBLEND, destination);
        return draw.ClipQuad(SkyOverhaul::Frame::kFarDepth, g_corners);
    }

    void Draw(IDirect3DDevice9* device, IDirect3DPixelShader9* shader,
              const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::CloudLayer::Lighting& lighting, const Light& light,
              const Values& v, float storminess) {
        // Kept clear of the layer below it however high that is set, so the two never interleave.
        const float above = v.cloudBase + v.cloudThickness + kCirrusClearance;
        const float cirrusAltitude = above > kCirrusFloor ? above : kCirrusFloor;

        const float shapeGrain = 1.0f / v.cloudSize;
        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            v.cloudBase, v.cloudThickness, v.cloudCoverage, v.cloudDensity,
            g_drift[0], g_drift[1], g_drift[0] * 0.5f, g_drift[1] * 0.5f,
            shapeGrain, shapeGrain * kDetailRepeats, shapeGrain * kWeatherRepeats, v.cloudDetail,
            light.direction[0], light.direction[1], light.direction[2], kForwardScatter,
            light.colour[0], light.colour[1], light.colour[2], kLightStride,
            lighting.ambientColour[0], lighting.ambientColour[1], lighting.ambientColour[2],
            storminess * kStormBaseShade,
            light.back[0], light.back[1], light.back[2], 0.0f,
            kMaxDistance, kFadeDistance, kMarchDistance, v.cloudHaze,
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogValues[0], view.fogValues[1], view.fogValues[2], 0.0f,
            view.fogHeightValues[0], view.fogHeightValues[1], view.fogHeightValues[2],
            view.fogHeightValues[3],
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f,
            cirrusAltitude, kCirrusGrain, v.cirrus, v.cirrusOpacity,
            v.contrails, kTrailWidth, kTrailBreak, 0.0f,
            light.glow[0], light.glow[1], light.glow[2], SkyOverhaul::SkyModel::Grey(storminess)};
        std::copy(std::begin(constants), std::end(constants), g_constants);
        for (int corner = 0; corner < 4; corner++) {
            std::copy_n(view.corners[corner], 3, g_corners[corner]);
        }

        // Blended the way the engine's cloud layer blended: colour already multiplied in, alpha
        // what survives.
        g_drawn = DrawSky(device, shader, D3DBLEND_ONE, D3DBLEND_SRCALPHA);
    }
}

void SkyOverhaul::Clouds::Install() {
    Noise::Start();
}

void SkyOverhaul::Clouds::OnScenePass(const Frame::Pass& pass) {
    // Every scene pass arrives here and only one of them is the sky, so the clock is read after
    // the test rather than before it: ticking on all of them would leave the heartbeat measuring
    // the gap between two passes instead of the time between two frames.
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    g_drawn = false;
    const float elapsed = g_clock.Lap();

    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    Camera::View view;
    CloudLayer::Lighting lighting;
    if (shader == nullptr || !Camera::Read(pass.device, view) || !CloudLayer::Latest(lighting) ||
        !Noise::Ensure(pass.device)) {
        return;
    }

    const float storminess = SkyModel::Storminess(lighting.storm);
    const Tuning::Values v = Weathered(Tuning::Current(), storminess);
    const Light light = ChooseLight(lighting, v, storminess);
    Advance(lighting, v, elapsed);
    Draw(pass.device, shader, view, lighting, light, v, storminess);

    if (g_heartbeat.Due(elapsed)) {
        LogPass(pass, view, light, v, lighting.storm, elapsed);
    }
}

bool SkyOverhaul::Clouds::DrawCover(IDirect3DDevice9* device, float left, float top, float right,
                                    float bottom, float depth) {
    IDirect3DPixelShader9* shader = g_enabled && g_drawn ? g_coverShader.Get(device) : nullptr;
    if (shader == nullptr) {
        return false;
    }
    DrawGuard guard(device, kFirstConstant, kConstantCount);
    Bind(device, shader);
    return guard.ClipQuad(depth, g_corners, left, top, right, bottom);
}

bool SkyOverhaul::Clouds::DrawMask(IDirect3DDevice9* device) {
    IDirect3DPixelShader9* shader = g_drawn ? g_maskShader.Get(device) : nullptr;
    if (shader == nullptr) {
        return false;
    }
    // As the engine's own mask clouds blend: the mask multiplied down by the cover.
    return DrawSky(device, shader, D3DBLEND_ZERO, D3DBLEND_INVSRCCOLOR);
}

bool SkyOverhaul::Clouds::DrawShadow(IDirect3DDevice9* device) {
    IDirect3DPixelShader9* shader = g_enabled && g_drawn ? g_shadowShader.Get(device) : nullptr;
    if (shader == nullptr) {
        return false;
    }
    DrawGuard guard(device, kFirstConstant, kConstantCount);
    Bind(device, shader);
    return guard.ClipQuad(0.0f, g_corners);
}

void SkyOverhaul::Clouds::ReleaseDeviceObjects() {
    Noise::ReleaseDeviceObjects();
    g_shader.Release();
    g_coverShader.Release();
    g_maskShader.Release();
    g_shadowShader.Release();
}

void SkyOverhaul::Clouds::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetMaskSubstitute(enabled ? &DrawMask : nullptr);
}
