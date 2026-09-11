#include "sky.h"

#include "sky_model.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/dome_draw.h"
#include "engine/fog_tint.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "sky_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them. The clouds use the same range: the two never draw in
    // one call, and each puts back what it found.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 6;

    // The far end of the depth range, where nothing but sky has been drawn. The dome is drawn with
    // a less-or-equal test against a cleared far plane, so this passes wherever no world stands and
    // is rejected wherever one does.
    constexpr float kSkyDepth = 1.0f;

    constexpr float kDegrees = 57.29578f;

    // The most the zenith may be lifted, and the height of the sun over which that lift is let go,
    // as sines of its elevation: held in full above twenty-five degrees, gone by five, so that
    // sunset still darkens the sky overhead the way it should.
    constexpr float kZenithHoldMax = 3.0f;
    constexpr float kZenithHoldLow = 0.087f;
    constexpr float kZenithHoldHigh = 0.423f;

    // What the sun is worth in the shader.
    constexpr float kSunIntensity = 41.4f;

    bool g_enabled = false;

    // What the last dome was drawn from and what the model made of it, kept for the heartbeat.
    struct Drawn {
        SkyOverhaul::Camera::View view;
        SkyOverhaul::CloudLayer::Lighting lighting;
        float towardColour[3];
        float awayColour[3];
        float zenithLift;
    };

    Drawn g_last = {};

    SkyOverhaul::PixelShader g_shader{"sky", g_skyPixelShader};
    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{5.0f};

    float Luminance(const float colour[3]) {
        return colour[0] * 0.299f + colour[1] * 0.587f + colour[2] * 0.114f;
    }

    float SmoothStep(float from, float to, float value) {
        const float t = std::clamp((value - from) / (to - from), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    // The engine's fog heading, flat and unit length: the direction its fog ramp starts from.
    void FogHeading(const SkyOverhaul::Camera::View& view, float out[3]) {
        float length = std::sqrt(view.fogColourVector[0] * view.fogColourVector[0] +
                                 view.fogColourVector[1] * view.fogColourVector[1]);
        if (length < 0.0001f) {
            length = 1.0f;
        }
        out[0] = view.fogColourVector[0] / length;
        out[1] = view.fogColourVector[1] / length;
        out[2] = 0.0f;
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
        Drawn drawn;
        if (shader == nullptr || !SkyOverhaul::Camera::Read(device, drawn.view) ||
            !SkyOverhaul::CloudLayer::Latest(drawn.lighting)) {
            return false;
        }
        const SkyOverhaul::Camera::View& view = drawn.view;
        const SkyOverhaul::CloudLayer::Lighting& lighting = drawn.lighting;

        // The weather is folded in here, so what crosses into the shader is already finished.
        const float storminess = SkyOverhaul::SkyModel::Storminess(lighting.storm);
        const float haze = SkyOverhaul::SkyModel::Haze(storminess);
        const float intensity = kSunIntensity * SkyOverhaul::SkyModel::SunShare(storminess);

        // What our air comes to at the horizon, along the engine's own fog heading and against it:
        // the two ends of the ramp whose hue the land's fog takes. Before the exposure, as the
        // engine's own fog colour is.
        float toward[3];
        FogHeading(view, toward);
        const float away[3] = {-toward[0], -toward[1], 0.0f};
        SkyOverhaul::SkyModel::Radiance(toward, lighting.sunDirection, view.eye[2], haze, intensity,
                                        drawn.towardColour);
        SkyOverhaul::SkyModel::Radiance(away, lighting.sunDirection, view.eye[2], haze, intensity,
                                        drawn.awayColour);
        SkyOverhaul::FogTint::SetHorizon(drawn.towardColour, drawn.awayColour);

        drawn.zenithLift = ZenithLift(lighting.sunDirection, view.eye[2], intensity);
        g_last = drawn;

        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            lighting.sunDirection[0], lighting.sunDirection[1], lighting.sunDirection[2],
            lighting.night,
            haze, intensity, drawn.zenithLift, SkyOverhaul::SkyModel::Grey(storminess),
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f};

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kConstantCount);
        device->SetPixelShader(shader);
        device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
        return guard.ClipQuad(kSkyDepth, view.corners);
    }
}

void SkyOverhaul::Sky::Install() {
    // The world's own fog colour is followed whether or not our sky is drawn: it is what the land
    // fades into, and a player who wants the horizon changed wants the land changed with it.
    FogTint::Install();
    DomeDraw::Install(&Draw);
}

void SkyOverhaul::Sky::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live || !g_heartbeat.Due(g_clock.Lap())) {
        return;
    }
    const Camera::View& view = g_last.view;
    const CloudLayer::Lighting& light = g_last.lighting;
    const float* sun = light.sunDirection;

    // The fog heading is the direction the engine's fog ramp starts from, measured against the sun:
    // near zero means the ramp's first colour is the sun's side, as its name says.
    float headingOffset = -1.0f;
    const float sunAcross = std::sqrt(sun[0] * sun[0] + sun[1] * sun[1]);
    if (sunAcross > 0.0001f) {
        float toward[3];
        FogHeading(view, toward);
        const float cosine = (toward[0] * sun[0] + toward[1] * sun[1]) / sunAcross;
        headingOffset = std::acos(std::clamp(cosine, -1.0f, 1.0f)) * kDegrees;
    }
    const float elevation = std::asin(std::clamp(sun[2], -1.0f, 1.0f)) * kDegrees;

    // Counts that stand still are the two ways this fails without anything else saying so: a dome
    // that stopped being recognised, and a fog colour that is never being reached.
    FCSE::Logf("sky f%u: %u domes replaced, %u fog uploads retinted | night %.2f storm %.2f "
               "exposure %.2f zenith x%.2f",
               pass.frame, DomeDraw::SubstituteCount(), FogTint::TintCount(), light.night,
               light.storm, view.bloom, g_last.zenithLift);
    FCSE::Logf("sky f%u: sun %+.1f deg, fog heading %.0f deg off it | model toward "
               "(%.3f %.3f %.3f) away (%.3f %.3f %.3f)",
               pass.frame, elevation, headingOffset, g_last.towardColour[0], g_last.towardColour[1],
               g_last.towardColour[2], g_last.awayColour[0], g_last.awayColour[1],
               g_last.awayColour[2]);
    FCSE::Logf("sky f%u: engine fog toward (%.3f %.3f %.3f) away (%.3f %.3f %.3f)", pass.frame,
               view.fogColour[0], view.fogColour[1], view.fogColour[2],
               view.fogColour[0] + view.fogColourRange[0],
               view.fogColour[1] + view.fogColourRange[1],
               view.fogColour[2] + view.fogColourRange[2]);
    // Distance: per metre, offset, amount. Height: per metre, offset, then the value at the bottom of
    // the height band and how much more the top adds - so the fog on the lowest geometry is the
    // amount times the third height value.
    FCSE::Logf("sky f%u: engine fog distance (%.5f %.3f %.3f) height (%.5f %.3f %.3f %.3f)",
               pass.frame, view.fogValues[0], view.fogValues[1], view.fogValues[2],
               view.fogHeightValues[0], view.fogHeightValues[1], view.fogHeightValues[2],
               view.fogHeightValues[3]);
    FCSE::Logf("sky f%u: cloud ambient (%.3f %.3f %.3f) moon (%.3f %.3f %.3f) moon z %+.2f",
               pass.frame, light.ambientColour[0], light.ambientColour[1], light.ambientColour[2],
               light.moonColour[0], light.moonColour[1], light.moonColour[2],
               light.moonDirection[2]);
}

void SkyOverhaul::Sky::ReleaseDeviceObjects() {
    g_shader.Release();
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
