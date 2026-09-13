#include "night.h"

#include "sky_model.h"

#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "tuning.h"

#include "night_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    using SkyOverhaul::Tuning::Values;

    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 2;

    // The shader reads its place from the rasteriser, so the quad's rays are unused.
    constexpr float kNoRays[4][3] = {};

    // The sun's height, as a sine, where the rods start to take over and where they have.
    constexpr float kDuskBegins = -0.10f;
    constexpr float kDuskEnds = -0.25f;

    // Where a pixel starts keeping any colour, as a share of the brightness at which it keeps all.
    constexpr float kKneeSoftness = 0.3f;

    // The grain at full noise as a share of that same brightness, the share of it a full moon's
    // light takes away, how often its pattern changes, and how many patterns it cycles through.
    constexpr float kGrainShare = 0.15f;
    constexpr float kMoonlitGrain = 0.5f;
    constexpr float kGrainHertz = 24.0f;
    constexpr float kGrainSteps = 256.0f;

    // Below this the grade changes nothing a player could see.
    constexpr float kFaintest = 0.002f;

    // With HDR on the world alternates between two formats frame to frame, so one copy of each.
    constexpr size_t kResolveCount = 2;

    struct Resolve {
        IDirect3DTexture9* texture;
        IDirect3DSurface9* surface;
        D3DSURFACE_DESC desc;
    };

    // How the eye sees this frame.
    struct Gate {
        float dusk;
        float moonlight;
        float drive;
        float drain;
        float grain;
    };

    bool g_enabled = false;

    IDirect3DDevice9* g_owner = nullptr;
    Resolve g_resolves[kResolveCount] = {};
    // Set once the device refuses a resolve; cleared on reset.
    bool g_refused = false;
    SkyOverhaul::PixelShader g_shader{"night", g_nightPixelShader};

    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{2.0f};
    float g_grainTime = 0.0f;

    void ReleaseResolves() {
        for (Resolve& resolve : g_resolves) {
            SkyOverhaul::Release(resolve.surface);
            SkyOverhaul::Release(resolve.texture);
        }
    }

    // A single-sampled copy matching `target`, made on first use.
    const Resolve* EnsureResolve(IDirect3DDevice9* device, const D3DSURFACE_DESC& target) {
        if (g_owner != device) {
            ReleaseResolves();
            g_owner = device;
        }
        Resolve* empty = nullptr;
        for (Resolve& resolve : g_resolves) {
            if (resolve.texture == nullptr) {
                empty = empty != nullptr ? empty : &resolve;
            } else if (resolve.desc.Width == target.Width && resolve.desc.Height == target.Height &&
                       resolve.desc.Format == target.Format) {
                return &resolve;
            }
        }
        if (empty == nullptr) {
            ReleaseResolves();
            empty = &g_resolves[0];
        }

        const HRESULT created =
            SkyOverhaul::CreateTarget(device, target, &empty->texture, &empty->surface);
        if (FAILED(created)) {
            g_refused = true;
            FCSE::Logf("night: no %ux%u copy of the world in format %u, 0x%08lX", target.Width,
                       target.Height, static_cast<unsigned>(target.Format),
                       static_cast<unsigned long>(created));
            return nullptr;
        }
        empty->desc = target;
        FCSE::Logf("night: grading %ux%u, format %u", target.Width, target.Height,
                   static_cast<unsigned>(target.Format));
        return empty;
    }

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

    // Resolves the world into a copy and paints it back over every sample the world drew.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const Gate& gate, const Values& v) {
        IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
        D3DSURFACE_DESC desc;
        if (g_refused || shader == nullptr || FAILED(pass.target->GetDesc(&desc))) {
            return false;
        }
        const Resolve* resolve = EnsureResolve(pass.device, desc);
        if (resolve == nullptr) {
            return false;
        }
        const HRESULT resolved =
            pass.device->StretchRect(pass.target, nullptr, resolve->surface, nullptr, D3DTEXF_NONE);
        if (FAILED(resolved)) {
            g_refused = true;
            FCSE::Logf("night: the world would not resolve, format %u, 0x%08lX",
                       static_cast<unsigned>(desc.Format), static_cast<unsigned long>(resolved));
            return false;
        }

        const float constants[kConstantCount * 4] = {
            gate.drain,
            gate.drive * v.nightPurkinje,
            v.nightColourAbove,
            v.nightColourAbove * kKneeSoftness,

            gate.grain,
            std::floor(g_grainTime * kGrainHertz),
            1.0f / static_cast<float>(desc.Width),
            1.0f / static_cast<float>(desc.Height)};

        SkyOverhaul::ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
        pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        pass.device->SetRenderState(D3DRS_ZFUNC, D3DCMP_GREATER);
        // The world target's alpha carries brightness for the bloom.
        pass.device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                                D3DCOLORWRITEENABLE_GREEN |
                                                                D3DCOLORWRITEENABLE_BLUE);
        pass.device->SetPixelShader(shader);
        pass.device->SetTexture(0, resolve->texture);
        pass.device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
        return draw.ClipQuad(SkyOverhaul::Frame::kFarDepth, kNoRays);
    }
}

void SkyOverhaul::Night::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const float elapsed = g_clock.Lap();
    g_grainTime = std::fmod(g_grainTime + elapsed, kGrainSteps / kGrainHertz);

    CloudLayer::Lighting lighting;
    if (!CloudLayer::Latest(lighting)) {
        return;
    }
    const Values v = Tuning::Current();
    const Gate gate = Measure(lighting, v);
    const bool drawn = (gate.drain > kFaintest || gate.grain > kFaintest) && Draw(pass, gate, v);

    if (g_heartbeat.Due(elapsed)) {
        FCSE::Logf("night f%u: sun %+.3f moon %+.3f storm %.2f night %.2f | dusk %.2f "
                   "moonlight %.2f drain %.2f grain %.4f | drawn %d",
                   pass.frame, lighting.sunDirection[2], lighting.moonDirection[2], lighting.storm,
                   lighting.night, gate.dusk, gate.moonlight, gate.drain, gate.grain, drawn ? 1 : 0);
    }
}

void SkyOverhaul::Night::ReleaseDeviceObjects() {
    ReleaseResolves();
    g_shader.Release();
    g_owner = nullptr;
    g_refused = false;
}

void SkyOverhaul::Night::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
