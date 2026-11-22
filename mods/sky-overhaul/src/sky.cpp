#include "sky.h"

#include "angles.h"
#include "blend.h"
#include "sky_model.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/dome_draw.h"
#include "engine/fog_tint.h"
#include "engine/frame.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/time_of_day.h"
#include "tuning.h"
#include "weather/weather.h"

#include "sky_land_ps.h"
#include "sky_ps.h"
#include "sky_table_ps.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <iterator>
#include <utility>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them. The clouds use the same range: the two never draw in
    // one call, and each puts back what it found.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 9;


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

    // The share of its haze the sky keeps through dusk, so the glow round the sun leaves it a disc.
    constexpr float kDuskHazeKept = 0.3f;

    // The sun's height, as a sine, over which the land's fog darkens for the dusk: from seven degrees
    // up to a degree and a half.
    constexpr float kDuskStart = 0.122f;
    constexpr float kDuskEnd = 0.026f;

    // How bright the filled land is drawn at each hour of the game's clock, against the fog's own.
    constexpr float kLandByHour[24] = {1.0f,  1.0f,  1.0f,  1.0f,  1.0f,  0.48f, 0.42f, 1.26f,
                                       1.21f, 1.01f, 1.06f, 1.03f, 1.2f,  1.24f, 1.23f, 1.16f,
                                       1.17f, 1.24f, 1.04f, 1.05f, 0.89f, 1.0f,  0.44f, 0.42f};

    // The air's colour by direction, as sky.fx reads it: wide enough across that the glow around the
    // sun stays round, and its rows spread by the square root of the elevation.
    constexpr UINT kTableWidth = 256;
    constexpr UINT kTableHeight = 128;
    // A sampler none of the sky pass's draws use.
    constexpr DWORD kTableSampler = 7;
    // TablePS's coordinates, nought to one across the table. Direct3D 9 puts a pixel's centre on its
    // integer coordinate, so the edges carry half a texel more for each texel to get its own centre.
    constexpr float kHalfX = 0.5f / kTableWidth;
    constexpr float kHalfY = 0.5f / kTableHeight;
    constexpr float kTableCorners[4][3] = {{kHalfX, kHalfY, 0.0f},
                                           {1.0f + kHalfX, kHalfY, 0.0f},
                                           {kHalfX, 1.0f + kHalfY, 0.0f},
                                           {1.0f + kHalfX, 1.0f + kHalfY, 0.0f}};

    SkyOverhaul::PixelShader g_shader{"sky", g_skyPixelShader};
    SkyOverhaul::PixelShader g_tableShader{"sky table", g_skyTablePixelShader};
    SkyOverhaul::PixelShader g_landShader{"sky land", g_skyLandPixelShader};

    bool g_enabled = false;
    // Whether our sky drew filled land since the last pass ended, and what it was drawn with.
    bool g_landDrawn = false;
    std::array<float, kConstantCount * 4> g_constants;
    float g_corners[4][3];

    SkyOverhaul::OwnedTarget g_table{"sky table", D3DFMT_A16B16G16R16F};
    // Whether the table holds a frame's air, and whether a dome since the last frame wants it drawn.
    bool g_tableReady = false;
    bool g_tableWanted = false;

    // The table on its own sampler for the length of the dome's draw, which has to leave every
    // sampler as it found it: the draws after the dome inherit what it leaves.
    class TableBinding {
    public:
        explicit TableBinding(IDirect3DDevice9* device) : m_device(device) {
            m_device->GetTexture(kTableSampler, &m_texture);
            for (size_t i = 0; i < std::size(kStates); i++) {
                m_device->GetSamplerState(kTableSampler, kStates[i].type, &m_states[i]);
                m_device->SetSamplerState(kTableSampler, kStates[i].type, kStates[i].value);
            }
            m_device->SetTexture(kTableSampler, g_table.texture);
        }

        ~TableBinding() {
            for (size_t i = 0; i < std::size(kStates); i++) {
                m_device->SetSamplerState(kTableSampler, kStates[i].type, m_states[i]);
            }
            m_device->SetTexture(kTableSampler, m_texture);
            SkyOverhaul::Release(m_texture);
        }

        TableBinding(const TableBinding&) = delete;
        TableBinding& operator=(const TableBinding&) = delete;

    private:
        struct State {
            D3DSAMPLERSTATETYPE type;
            DWORD value;
        };
        static constexpr State kStates[] = {
            {D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP}, {D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP},
            {D3DSAMP_MAGFILTER, D3DTEXF_LINEAR},   {D3DSAMP_MINFILTER, D3DTEXF_LINEAR},
            {D3DSAMP_MIPFILTER, D3DTEXF_NONE},     {D3DSAMP_SRGBTEXTURE, FALSE},
        };

        IDirect3DDevice9* m_device;
        IDirect3DBaseTexture9* m_texture = nullptr;
        DWORD m_states[std::size(kStates)] = {};
    };

    // The filled land's brightness at `hour` of the clock, blended between the hours either side.
    float LandBrightness(float hour) {
        const int before = static_cast<int>(hour) % 24;
        const float after = kLandByHour[(before + 1) % 24];
        return kLandByHour[before] + (after - kLandByHour[before]) * (hour - std::floor(hour));
    }

    using SkyOverhaul::Luma;
    using SkyOverhaul::Smooth;

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
        const float nowLuminance = Luma(now);
        const float lift =
            nowLuminance > 1.0e-6f
                ? std::clamp(Luma(overhead) / nowLuminance, 1.0f, kZenithHoldMax)
                : kZenithHoldMax;
        const float kept = Smooth(kZenithHoldLow, kZenithHoldHigh, sun[2]);
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
        const SkyOverhaul::Weather::Air air = SkyOverhaul::Weather::AirNow(lighting.storm);
        const float storminess = air.storminess;
        const float intensity =
            kSunIntensity * SkyOverhaul::SkyModel::SunShare(storminess) *
            SkyOverhaul::SkyModel::DaylightCut(lighting.sunDirection[2], storminess, kDaylight) *
            SkyOverhaul::SkyModel::ThroughHaze(lighting.sunDirection[2],
                                               air.haze - SkyOverhaul::SkyModel::Haze(storminess));
        // Thinned through dusk unless a storm greys the sky.
        const float haze =
            air.haze * (1.0f - (1.0f - kDuskHazeKept) *
                                   SkyOverhaul::SkyModel::Dusk(lighting.sunDirection[2]) *
                                   (1.0f - storminess));

        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        float hour = 12.0f;
        SkyOverhaul::TimeOfDay::Hour(hour);
        SkyOverhaul::FogTint::SetDusk(Smooth(kDuskStart, kDuskEnd, lighting.sunDirection[2]),
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
            lighting.moonDirection[0], lighting.moonDirection[1], lighting.moonDirection[2], 0.0f,
            air.milk, air.veil, 0.0f, 0.0f};
        std::copy_n(&view.corners[0][0], 12, &g_corners[0][0]);

        // The frame's air goes into the table at its end, for the next frame's dome; until a table
        // holds any, the engine's dome draws.
        g_tableWanted = true;
        if (!g_tableReady) {
            return false;
        }

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kConstantCount);
        TableBinding table(device);
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
    // Once the night is full the engine draws only its stars, so the sky goes over them here with
    // the blend the dome would have given it, before the clouds.
    if (pass.sky && pass.live && DomeDraw::SkyOwed()) {
        ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
        pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        pass.device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        pass.device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
        pass.device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
        Draw(pass.device);
    }
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

void SkyOverhaul::Sky::OnFinalPass(const Frame::Pass& pass) {
    if (!std::exchange(g_tableWanted, false)) {
        return;
    }
    IDirect3DPixelShader9* shader = g_tableShader.Get(pass.device);
    if (shader == nullptr || !g_table.Ensure(pass.device, kTableWidth, kTableHeight)) {
        return;
    }
    ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
    pass.device->SetPixelShader(shader);
    pass.device->SetPixelShaderConstantF(kFirstConstant, g_constants.data(), kConstantCount);
    AwayFromPass away(pass);
    pass.device->SetRenderTarget(0, g_table.surface);
    g_tableReady = draw.ClipQuad(0.0f, kTableCorners);
}

void SkyOverhaul::Sky::Fill(const Tuning::Values& values, float fill[2]) {
    fill[0] = g_enabled ? values.horizonFill : 0.0f;
    fill[1] = std::sin(Radians(values.horizonFillHeight));
}

void SkyOverhaul::Sky::ReleaseDeviceObjects() {
    g_shader.Release();
    g_tableShader.Release();
    g_landShader.Release();
    g_table.Release();
    g_tableReady = false;
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
