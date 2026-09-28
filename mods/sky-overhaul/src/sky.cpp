#include "sky.h"

#include "sky_model.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/dome_draw.h"
#include "engine/fog_tint.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"

#include "sky_ps.h"

#include <algorithm>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them. The clouds use the same range: the two never draw in
    // one call, and each puts back what it found.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 6;

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

    SkyOverhaul::PixelShader g_shader{"sky", g_skyPixelShader};

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

        SkyOverhaul::FogTint::SetDusk(SmoothStep(kDuskStart, kDuskEnd, lighting.sunDirection[2]),
                                      SkyOverhaul::Tuning::Current().duskFogBrightness,
                                      lighting.sunDirection);

        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            lighting.sunDirection[0], lighting.sunDirection[1], lighting.sunDirection[2],
            lighting.night,
            haze, intensity, ZenithLift(lighting.sunDirection, view.eye[2], intensity),
            SkyOverhaul::SkyModel::Grey(storminess),
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f};

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kConstantCount);
        device->SetPixelShader(shader);
        device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
        return guard.ClipQuad(SkyOverhaul::Frame::kFarDepth, view.corners);
    }
}

void SkyOverhaul::Sky::Install() {
    // The world's own fog colour is followed whether or not our sky is drawn: it is what the land
    // fades into, and a player who wants the horizon changed wants the land changed with it.
    FogTint::Install();
    DomeDraw::Install(&Draw);
}

void SkyOverhaul::Sky::ReleaseDeviceObjects() {
    g_shader.Release();
}

void SkyOverhaul::Sky::SetEnabled(bool enabled) {
    DomeDraw::SetMode(enabled ? DomeDraw::Mode::Overhaul : DomeDraw::Mode::Engine);
    // With the engine drawing its own sky there is nothing for the world's fog to agree with, so it
    // goes back to the colour the engine chose.
    if (!enabled) {
        FogTint::Forget();
    }
}
