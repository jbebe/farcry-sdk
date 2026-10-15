#include "night.h"

#include "sky_model.h"

#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/frame_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"

#include "night_ps.h"

#include <algorithm>

namespace {
    using SkyOverhaul::Tuning::Values;

    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 2;

    // The sun's height, as a sine, where the rods start to take over and where they have.
    constexpr float kDuskBegins = -0.10f;
    constexpr float kDuskEnds = -0.25f;

    // Where a pixel starts keeping any colour, as a share of the brightness at which it keeps all.
    constexpr float kKneeSoftness = 0.3f;

    // The grain at full noise as a share of that same brightness, and the share of it a full
    // moon's light takes away.
    constexpr float kGrainShare = 0.15f;
    constexpr float kMoonlitGrain = 0.5f;

    // Below this the grade changes nothing a player could see.
    constexpr float kFaintest = 0.002f;

    // How the eye sees this frame.
    struct Gate {
        float dusk;
        float moonlight;
        float drive;
        float drain;
        float grain;
    };

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"night", g_nightPixelShader};

    SkyOverhaul::GrainClock g_grain;

    Gate Measure(const SkyOverhaul::CloudLayer::Lighting& lighting, const Values& v) {
        Gate gate = {};
        gate.dusk = std::clamp((lighting.sunDirection[2] - kDuskBegins) / (kDuskEnds - kDuskBegins),
                               0.0f, 1.0f);

        // A storm's cloud keeps the moon's light off the ground as it keeps the sun's.
        const float storminess = SkyOverhaul::SkyModel::Storminess(lighting.storm);
        gate.moonlight = SkyOverhaul::SkyModel::MoonRise(lighting.moonDirection[2]) *
                         SkyOverhaul::SkyModel::SunShare(storminess);

        gate.drive = v.nightStrength * gate.dusk;
        gate.drain = gate.drive * (1.0f - v.nightMoonColour * gate.moonlight);
        gate.grain = gate.drive * v.nightNoise * v.nightColourAbove * kGrainShare *
                     (1.0f - kMoonlitGrain * gate.moonlight);
        return gate;
    }

    // Copies the world and paints it back over every sample the world drew.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const Gate& gate, const Values& v) {
        IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
        IDirect3DTexture9* world = shader != nullptr ? SkyOverhaul::FrameCopy::Take(pass) : nullptr;
        if (world == nullptr) {
            return false;
        }

        const float constants[kConstantCount * 4] = {
            gate.drain,
            gate.drive * v.nightPurkinje,
            v.nightColourAbove,
            v.nightColourAbove * kKneeSoftness,

            gate.grain,
            g_grain.Pattern(),
            1.0f / static_cast<float>(pass.backBuffer.Width),
            1.0f / static_cast<float>(pass.backBuffer.Height)};

        SkyOverhaul::ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
        pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        pass.device->SetRenderState(D3DRS_ZFUNC, D3DCMP_GREATER);
        // The world target's alpha carries brightness for the bloom.
        pass.device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                                D3DCOLORWRITEENABLE_GREEN |
                                                                D3DCOLORWRITEENABLE_BLUE);
        pass.device->SetPixelShader(shader);
        pass.device->SetTexture(0, world);
        pass.device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
        return draw.ClipQuad(SkyOverhaul::Frame::kFarDepth);
    }
}

void SkyOverhaul::Night::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    g_grain.Advance();

    CloudLayer::Lighting lighting;
    if (!CloudLayer::Latest(lighting)) {
        return;
    }
    const Values v = Tuning::Current();
    const Gate gate = Measure(lighting, v);
    if (gate.drain > kFaintest || gate.grain > kFaintest) {
        Draw(pass, gate, v);
    }
}

void SkyOverhaul::Night::ReleaseDeviceObjects() {
    g_shader.Release();
}

void SkyOverhaul::Night::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
