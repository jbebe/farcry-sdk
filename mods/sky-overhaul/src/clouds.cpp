#include "clouds.h"

#include "cell_map.h"
#include "depth_constants.h"
#include "hash.h"
#include "lightning.h"
#include "sky.h"
#include "sky_model.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/depth_texture.h"
#include "engine/dome_draw.h"
#include "engine/environment.h"
#include "engine/noise.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/time_of_day.h"
#include "tuning.h"
#include "weather/weather.h"

#include "clouds_composite_ps.h"
#include "clouds_cover_ps.h"
#include "clouds_half_ps.h"
#include "clouds_mask_ps.h"
#include "clouds_shadow_ps.h"
#include "clouds_sunlit_ps.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them.
    constexpr UINT kFirstConstant = 71;
    // The trails' registers come last.
    constexpr UINT kTrailCount = 3;
    constexpr UINT kConstantCount = 17 + kTrailCount;
    // The half-resolution march also reads the depth's registers, which follow the clouds' own, and
    // the lightning's after those.
    constexpr UINT kFlashConstant =
        SkyOverhaul::DepthConstants::kFirst + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kHalfConstantCount =
        kFlashConstant + SkyOverhaul::Lightning::kFlashes - kFirstConstant;
    // Where CompositePS reads the march, past the noise and the depth, and where the cells are read.
    constexpr DWORD kHalfSampler = 4;
    constexpr DWORD kCellSampler = 5;

    constexpr double kPi = 3.14159265358979;

    // How far out clouds are drawn, over how much of the last of that they fade away, and how far
    // the march itself runs. A layer is a plane, so a ray near the horizon would otherwise run for
    // ever, and the samples would be spread so thin they stepped past whole clouds.
    constexpr float kMaxDistance = 30000.0f;
    constexpr float kFadeDistance = 22000.0f;
    constexpr float kMarchDistance = 8000.0f;

    // Metres a clock second the layer drifts at a wind of one. The clock runs ten times faster than play.
    constexpr double kWindSpeed = 0.9;

    // How far apart the samples toward the light are. Wide enough that five of them reach through a
    // whole cloud, which is what a shadow inside one needs.
    constexpr float kLightStride = 90.0f;

    // The sun's height, as a sine, below which no layer can see it and the moon lights the clouds.
    constexpr float kSunGone = -0.07f;
    // How much further the sun sinks, as a sine, while the moonlight comes up to full.
    constexpr float kMoonRising = 0.13f;

    // How bright sunlight is on the clouds where our air lets all of it through, and what a clear
    // day's high sun is cut to so their lit faces no longer clip.
    constexpr float kSunlight = 6.6f;
    constexpr float kDaylight = 0.25f;

    // Moonlight at full strength, the share of either light a thin edge lets through, and how
    // brightly the air glows around the moon.
    constexpr float kMoonColour[3] = {1.6f, 1.8f, 2.0f};
    constexpr float kBackShare = 0.13f;
    constexpr float kMoonGlow = 0.1f;
    // How much of the moonlight on the clouds a fully covered sky takes, the layer shading itself.
    constexpr float kCoveredMoon = 0.5f;

    // What the game's full storm makes of the layer's coverage and water and of the high sheet's
    // opacity, with the weather off.
    constexpr float kStormCoverage = 0.65f;
    constexpr float kStormDensity = 0.09f;
    constexpr float kStormCirrusOpacity = 0.8f;

    // How deep the layer is at a tallness of nought, and how much deeper at one, against the tuned
    // depth that a half gives.
    constexpr float kFlatDepth = 0.4f;
    constexpr float kTallDepth = 1.2f;

    // How much of its distance a cloud keeps before it turns into the horizon, once the weather's fog
    // swallows the land or the night is full: rain, fog and the dark hide far cloud as they hide far
    // land.
    constexpr float kVeiledCloudHaze = 0.1f;

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

    // How far across the campaign world a trail's point is rolled from, in metres, and the seed the
    // rolls are hashed with.
    constexpr float kWorldExtent = 5120.0f;
    constexpr uint32_t kTrailSeed = 0x7A11u;

    // How much light bends forward off a droplet, and the two frequencies the detail and the
    // weather are read at relative to the shape.
    constexpr float kForwardScatter = 0.55f;
    constexpr float kDetailRepeats = 11.0f;
    constexpr float kWeatherRepeats = 0.18f;

    using SkyOverhaul::Tuning::Values;

    bool g_enabled = false;

    // What the clouds were last drawn with, which the sun's cover is drawn through again.
    float g_constants[kConstantCount * 4] = {};
    float g_corners[4][3] = {};
    bool g_drawn = false;
    IDirect3DTexture9* g_cellMap = nullptr;

    SkyOverhaul::PixelShader g_halfShader{"clouds half", g_cloudsHalfPixelShader};
    SkyOverhaul::PixelShader g_compositeShader{"clouds composite", g_cloudsCompositePixelShader};
    SkyOverhaul::PixelShader g_coverShader{"clouds cover", g_cloudsCoverPixelShader};
    SkyOverhaul::PixelShader g_maskShader{"clouds mask", g_cloudsMaskPixelShader};
    SkyOverhaul::PixelShader g_shadowShader{"clouds shadow", g_cloudsShadowPixelShader};
    SkyOverhaul::PixelShader g_sunlitShader{"clouds sunlit", g_cloudsSunlitPixelShader};

    // The march at half resolution: colour premultiplied, alpha what survives, as the layer blends.
    SkyOverhaul::OwnedTarget g_half{"clouds", D3DFMT_A16B16G16R16F};
    SkyOverhaul::OwnedTarget g_sunlit{"clouds sunlit", D3DFMT_A16B16G16R16F};

    // The one light the shader marches toward, and the glow of the air around the moon.
    struct Light {
        float direction[3];
        float colour[3];
        float back[3];
        float glow[3];
    };

    // How far the moon has taken over lighting the clouds from the sun, nought to one.
    float MoonShare(float sunHeight) {
        return std::clamp((kSunGone - sunHeight) / kMoonRising, 0.0f, 1.0f);
    }

    // The tuned values as the air mass has them, the cells being drawn from the map on top, or
    // carried toward an overcast sky as the game's storm rises: more of the sky filled, more water,
    // the high sheet across all of it, and no aircraft above. Fog and the night hide far cloud.
    Values Weathered(Values v, const SkyOverhaul::Weather::Air& air, float night) {
        v.cloudHaze *= 1.0f + (kVeiledCloudHaze - 1.0f) * (std::max)(air.veil, night);
        if (air.weather) {
            const SkyOverhaul::Weather::Levers& levers = air.sky;
            v.cloudCoverage = levers.coverage;
            v.cloudDensity = levers.density;
            v.cloudThickness *= kFlatDepth + kTallDepth * levers.tallness;
            v.cirrus = levers.cirrus;
            v.cirrusOpacity = levers.cirrusOpacity;
            v.contrails = levers.contrails;
            return v;
        }
        const float storminess = air.storminess;
        v.cloudCoverage += (kStormCoverage - v.cloudCoverage) * storminess;
        v.cloudDensity += (kStormDensity - v.cloudDensity) * storminess;
        v.cirrus += (1.0f - v.cirrus) * storminess;
        v.cirrusOpacity += (kStormCirrusOpacity - v.cirrusOpacity) * storminess;
        v.contrails -= v.contrails * storminess;
        return v;
    }

    // The sun until it has set for every layer, then the moon, coming up as the sun sinks further
    // and as the moon itself climbs, dimmer the more cloud there is. The sun's light is what our own
    // air lets through to the middle of the layer, dimmed as the sky is in a storm.
    Light ChooseLight(const SkyOverhaul::CloudLayer::Lighting& lighting, const Values& v,
                      const SkyOverhaul::Weather::Air& air) {
        const float storminess = air.storminess;
        const bool sun = lighting.sunDirection[2] > kSunGone;
        const float moon = MoonShare(lighting.sunDirection[2]) *
                           SkyOverhaul::SkyModel::MoonRise(lighting.moonDirection[2]);

        float sunlight[3];
        SkyOverhaul::SkyModel::Sunlight(lighting.sunDirection,
                                        v.cloudBase + 0.5f * v.cloudThickness, air.haze, sunlight);
        const float sunBrightness =
            kSunlight * SkyOverhaul::SkyModel::SunShare(storminess) *
            SkyOverhaul::SkyModel::DaylightCut(lighting.sunDirection[2], storminess, kDaylight);
        const float moonlight = moon * (1.0f - kCoveredMoon * air.cover);

        Light light;
        for (int c = 0; c < 3; c++) {
            light.direction[c] = sun ? lighting.sunDirection[c] : lighting.moonDirection[c];
            light.colour[c] = sun ? sunlight[c] * sunBrightness : kMoonColour[c] * moonlight;
            light.back[c] = light.colour[c] * kBackShare;
            light.glow[c] = kMoonColour[c] * moon * kMoonGlow;
        }
        return light;
    }

    // Where the layer has drifted to, in metres, by `gameSeconds`.
    void Drift(double gameSeconds, const Values& v, float out[2]) {
        double drift[2];
        SkyOverhaul::Weather::Drift(gameSeconds, kWindSpeed * v.cloudWind, drift);
        out[0] = static_cast<float>(drift[0]);
        out[1] = static_cast<float>(drift[1]);
    }

    // Day `day`'s aircraft trails, as the shader's registers take them: one to three, each through a
    // random point over the world in a random direction.
    void RollTrails(int day, float out[kTrailCount * 4]) {
        using SkyOverhaul::Random;
        const int count = 1 + (std::min)(static_cast<int>(Random(day, 0, 0, kTrailSeed) * 3.0f), 2);
        for (int i = 0; i < static_cast<int>(kTrailCount); i++) {
            float* line = out + i * 4;
            const float heading = Random(day, i, 1, kTrailSeed) * static_cast<float>(kPi);
            const float x = Random(day, i, 2, kTrailSeed) * kWorldExtent;
            const float y = Random(day, i, 3, kTrailSeed) * kWorldExtent;
            line[0] = -std::sin(heading);
            line[1] = std::cos(heading);
            line[2] = (line[0] * x + line[1] * y) * kCirrusGrain;
            line[3] = i < count ? 0.05f + 0.9f * Random(day, i, 4, kTrailSeed) : 0.0f;
        }
    }

    // The shader, this frame's constants, the noise, sampled the way the shape expects, and the
    // cells, clamped to the map.
    void Bind(IDirect3DDevice9* device, IDirect3DPixelShader9* shader) {
        device->SetPixelShader(shader);
        device->SetPixelShaderConstantF(kFirstConstant, g_constants, kConstantCount);

        device->SetTexture(0, SkyOverhaul::Noise::Shape());
        device->SetTexture(1, SkyOverhaul::Noise::Detail());
        device->SetTexture(2, SkyOverhaul::Noise::Weather());
        device->SetTexture(kCellSampler, g_cellMap);
        for (DWORD sampler : {0ul, 1ul, 2ul, kCellSampler}) {
            const DWORD address = sampler == kCellSampler ? D3DTADDRESS_CLAMP : D3DTADDRESS_WRAP;
            device->SetSamplerState(sampler, D3DSAMP_ADDRESSU, address);
            device->SetSamplerState(sampler, D3DSAMP_ADDRESSV, address);
            device->SetSamplerState(sampler, D3DSAMP_ADDRESSW, address);
            device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
            device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
            device->SetSamplerState(sampler, D3DSAMP_MIPFILTER, D3DTEXF_LINEAR);
        }
    }

    // The march into the half-resolution target where there is sky near, then laid over the sky at
    // full resolution, tested against the world's own depth, so the skyline and the trees keep
    // every pixel.
    bool DrawHalf(const SkyOverhaul::Frame::Pass& pass, IDirect3DPixelShader9* shader,
                  const SkyOverhaul::Camera::View& view,
                  const SkyOverhaul::DepthTexture::Found& depth) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* composite = g_compositeShader.Get(device);
        if (composite == nullptr || !g_half.Ensure(device, (pass.backBuffer.Width + 1) / 2,
                                                   (pass.backBuffer.Height + 1) / 2)) {
            return false;
        }
        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kHalfConstantCount);
        Bind(device, shader);
        SkyOverhaul::DepthConstants::Set(device, view, depth, pass.backBuffer.Width,
                                         pass.backBuffer.Height);
        float flashes[SkyOverhaul::Lightning::kFlashes * 4];
        SkyOverhaul::Lightning::Flashes(flashes);
        device->SetPixelShaderConstantF(kFlashConstant, flashes, SkyOverhaul::Lightning::kFlashes);
        {
            SkyOverhaul::AwayFromPass away(pass);
            device->SetRenderTarget(0, g_half.surface);
            if (!draw.ClipQuad(0.0f, g_corners)) {
                return false;
            }
        }
        device->SetPixelShader(composite);
        device->SetTexture(kHalfSampler, g_half.texture);
        device->SetSamplerState(kHalfSampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(kHalfSampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCALPHA);
        return draw.ClipQuad(SkyOverhaul::Frame::kFarDepth, g_corners);
    }

    void Draw(const SkyOverhaul::Frame::Pass& pass, IDirect3DPixelShader9* shader,
              const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::CloudLayer::Lighting& lighting, const Light& light,
              const Values& v, const SkyOverhaul::Weather::Air& air, const float drift[2],
              const float trails[kTrailCount * 4], const SkyOverhaul::DepthTexture::Found& depth) {
        // Kept clear of the layer below it however high that is set, so the two never interleave.
        const float above = v.cloudBase + v.cloudThickness + kCirrusClearance;
        const float cirrusAltitude = above > kCirrusFloor ? above : kCirrusFloor;

        const float shapeGrain = 1.0f / v.cloudSize;
        float fill[2];
        SkyOverhaul::Sky::Fill(v, fill);
        const float halfWidth = static_cast<float>((pass.backBuffer.Width + 1) / 2);
        const float pixelAngle = 2.0f / (view.horizontalScale * halfWidth);
        // The sky's fog by height as the game sets it, not as the weather reshapes it for the land.
        float skyFog[4];
        if (!SkyOverhaul::Environment::SkyFogHeight(skyFog)) {
            std::copy_n(view.fogHeightValues, 4, skyFog);
        }
        const float constants[(kConstantCount - kTrailCount) * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            v.cloudBase, v.cloudThickness, v.cloudCoverage, v.cloudDensity,
            drift[0], drift[1], drift[0] * 0.5f, drift[1] * 0.5f,
            shapeGrain, shapeGrain * kDetailRepeats, shapeGrain * kWeatherRepeats, v.cloudDetail,
            light.direction[0], light.direction[1], light.direction[2], kForwardScatter,
            light.colour[0], light.colour[1], light.colour[2], kLightStride,
            lighting.ambientColour[0], lighting.ambientColour[1], lighting.ambientColour[2],
            air.shade,
            light.back[0], light.back[1], light.back[2], pixelAngle,
            kMaxDistance, kFadeDistance, kMarchDistance, v.cloudHaze,
            view.fogColour[0], view.fogColour[1], view.fogColour[2], air.veil,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogValues[0], view.fogValues[1], view.fogValues[2], 0.0f,
            skyFog[0], skyFog[1], skyFog[2], skyFog[3],
            view.fogColourVector[0], view.fogColourVector[1], fill[0], fill[1],
            cirrusAltitude, kCirrusGrain, v.cirrus, v.cirrusOpacity,
            v.contrails, kTrailWidth, kTrailBreak, 0.0f,
            light.glow[0], light.glow[1], light.glow[2], SkyOverhaul::SkyModel::Grey(air.storminess)};
        std::copy_n(trails, kTrailCount * 4,
                    std::copy(std::begin(constants), std::end(constants), g_constants));
        for (int corner = 0; corner < 4; corner++) {
            std::copy_n(view.corners[corner], 3, g_corners[corner]);
        }

        // Blended the way the engine's cloud layer blended: colour already multiplied in, alpha
        // what survives.
        g_drawn = DrawHalf(pass, shader, view, depth);
        if (g_drawn) {
            SkyOverhaul::CloudLayer::Replaced();
        }
    }
}

void SkyOverhaul::Clouds::Install() {
    TimeOfDay::Install();
    Noise::Start();
}

void SkyOverhaul::Clouds::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    g_drawn = false;

    IDirect3DPixelShader9* shader = g_halfShader.Get(pass.device);
    Camera::View view;
    CloudLayer::Lighting lighting;
    DepthTexture::Found depth = {};
    if (shader == nullptr || !Camera::Read(pass.device, view) || !CloudLayer::Latest(lighting) ||
        !Noise::Ensure(pass.device) || !DepthTexture::Latest(depth)) {
        return;
    }
    g_cellMap = CellMap::Ensure(pass.device);
    if (g_cellMap == nullptr) {
        return;
    }

    const Weather::Air air = Weather::AirNow(lighting.storm);
    const Tuning::Values v = Weathered(Tuning::Current(), air, MoonShare(lighting.sunDirection[2]));
    const Light light = ChooseLight(lighting, v, air);
    double gameSeconds = 0.0;
    float drift[2] = {};
    if (Weather::Clock(gameSeconds) || TimeOfDay::Elapsed(gameSeconds)) {
        Drift(gameSeconds, v, drift);
    }
    const int day = static_cast<int>(gameSeconds / 86400.0);
    float trails[kTrailCount * 4];
    RollTrails(day, trails);
    Draw(pass, shader, view, lighting, light, v, air, drift, trails, depth);
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
    // Over the sky, tested against the world's own depth, which the pass still owns, and blended as
    // the engine's own mask clouds blend: the mask multiplied down by the cover.
    ScreenDraw draw(device, kFirstConstant, kConstantCount);
    Bind(device, shader);
    device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCCOLOR);
    return draw.ClipQuad(Frame::kFarDepth, g_corners);
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

IDirect3DTexture9* SkyOverhaul::Clouds::Sunlit(const Frame::Pass& pass, const float sun[3]) {
    IDirect3DPixelShader9* shader =
        g_enabled && g_drawn && sun[2] > kSunGone ? g_sunlitShader.Get(pass.device) : nullptr;
    if (shader == nullptr || !g_sunlit.Ensure(pass.device, 1, 1)) {
        return nullptr;
    }
    ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
    Bind(pass.device, shader);
    const float toward[4][3] = {{sun[0], sun[1], sun[2]}, {sun[0], sun[1], sun[2]},
                                {sun[0], sun[1], sun[2]}, {sun[0], sun[1], sun[2]}};
    AwayFromPass away(pass);
    pass.device->SetRenderTarget(0, g_sunlit.surface);
    return draw.ClipQuad(0.0f, toward) ? g_sunlit.texture : nullptr;
}

void SkyOverhaul::Clouds::ReleaseDeviceObjects() {
    Noise::ReleaseDeviceObjects();
    CellMap::ReleaseDeviceObjects();
    g_cellMap = nullptr;
    g_halfShader.Release();
    g_compositeShader.Release();
    g_coverShader.Release();
    g_maskShader.Release();
    g_shadowShader.Release();
    g_sunlitShader.Release();
    g_half.Release();
    g_sunlit.Release();
}

void SkyOverhaul::Clouds::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetMaskSubstitute(enabled ? &DrawMask : nullptr);
    DomeDraw::SetWatchDepth(DomeDraw::DepthUser::Clouds, enabled);
}
