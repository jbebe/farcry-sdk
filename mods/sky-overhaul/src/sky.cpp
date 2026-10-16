#include "sky.h"

#include "sky_model.h"

#include "depth_constants.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/depth_texture.h"
#include "engine/dome_draw.h"
#include "engine/fog_tint.h"
#include "engine/frame.h"
#include "engine/frame_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"

#include "sky_ps.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them. The clouds use the same range: the two never draw in
    // one call, and each puts back what it found.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 7;
    // Through the depth's registers, which the fill writes too.
    constexpr UINT kGuardedCount =
        SkyOverhaul::DepthConstants::kFirst + SkyOverhaul::DepthConstants::kCount - kFirstConstant;

    constexpr DWORD kWorldSampler = 0;

    constexpr float kRadiansPerDegree = 3.14159265f / 180.0f;

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

    // A sampler's texture and the states the fill sets on it, put back when this goes out of scope.
    class SamplerGuard {
    public:
        SamplerGuard(IDirect3DDevice9* device, DWORD sampler) : m_device(device), m_sampler(sampler) {
            device->GetTexture(sampler, &m_texture);
            for (size_t i = 0; i < std::size(kStates); i++) {
                device->GetSamplerState(sampler, kStates[i], &m_values[i]);
            }
        }

        ~SamplerGuard() {
            m_device->SetTexture(m_sampler, m_texture);
            for (size_t i = 0; i < std::size(kStates); i++) {
                m_device->SetSamplerState(m_sampler, kStates[i], m_values[i]);
            }
            SkyOverhaul::Release(m_texture);
        }

        SamplerGuard(const SamplerGuard&) = delete;
        SamplerGuard& operator=(const SamplerGuard&) = delete;

        // Exact texels, held at the edges.
        void Point() {
            m_device->SetSamplerState(m_sampler, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
            m_device->SetSamplerState(m_sampler, D3DSAMP_MINFILTER, D3DTEXF_POINT);
            m_device->SetSamplerState(m_sampler, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
            m_device->SetSamplerState(m_sampler, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
        }

    private:
        static constexpr D3DSAMPLERSTATETYPE kStates[] = {D3DSAMP_MAGFILTER, D3DSAMP_MINFILTER,
                                                         D3DSAMP_ADDRESSU, D3DSAMP_ADDRESSV};
        IDirect3DDevice9* m_device;
        DWORD m_sampler;
        IDirect3DBaseTexture9* m_texture = nullptr;
        DWORD m_values[std::size(kStates)] = {};
    };

    // Binds the world drawn so far and its depth for the land's fill. False leaves the sky to fill
    // below the horizon alone, as before the world or its depth has been seen.
    bool BindFill(IDirect3DDevice9* device, const SkyOverhaul::Camera::View& view) {
        SkyOverhaul::DepthTexture::Found depth = {};
        IDirect3DSurface9* target = nullptr;
        if (!SkyOverhaul::DepthTexture::Latest(depth) ||
            FAILED(device->GetRenderTarget(0, &target)) || target == nullptr) {
            return false;
        }
        // A water reflection draws a sky too, into a target the depth was not drawn for.
        D3DSURFACE_DESC desc = {};
        D3DSURFACE_DESC depthDesc = {};
        const bool matches = SUCCEEDED(target->GetDesc(&desc)) &&
                             SUCCEEDED(depth.texture->GetLevelDesc(0, &depthDesc)) &&
                             desc.Width == depthDesc.Width && desc.Height == depthDesc.Height;
        SkyOverhaul::Frame::Pass pass = {};
        pass.device = device;
        pass.target = target;
        IDirect3DTexture9* world = matches ? SkyOverhaul::FrameCopy::Take(pass) : nullptr;
        target->Release();
        if (world == nullptr) {
            return false;
        }
        SkyOverhaul::DepthConstants::Set(device, view, depth, desc.Width, desc.Height);
        device->SetTexture(kWorldSampler, world);
        return true;
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

        float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            lighting.sunDirection[0], lighting.sunDirection[1], lighting.sunDirection[2],
            lighting.night,
            haze, intensity, ZenithLift(lighting.sunDirection, view.eye[2], intensity),
            SkyOverhaul::SkyModel::Grey(storminess),
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 0.0f};

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kGuardedCount);
        SamplerGuard worldSampler(device, kWorldSampler);
        SamplerGuard depthSampler(device, SkyOverhaul::DepthConstants::kSampler);
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        if (v.horizonFill > 0.0f && BindFill(device, view)) {
            worldSampler.Point();
            depthSampler.Point();
            constants[(kConstantCount - 1) * 4] = v.horizonFill;
            constants[(kConstantCount - 1) * 4 + 1] =
                std::sin(v.horizonFillHeight * kRadiansPerDegree);
        }
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
