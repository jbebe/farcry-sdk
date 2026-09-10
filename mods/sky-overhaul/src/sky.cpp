#include "sky.h"

#include "sky_model.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/dome_draw.h"
#include "engine/fog_tint.h"
#include "engine/log.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "sky_ps.h"
#include "sky_vs.h"

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

    constexpr float kHeartbeatSeconds = 5.0f;

    // What a full storm does to the air: several times the haze, and rather less of the sun
    // reaching it. That is what an overcast sky is, before a single cloud is drawn.
    constexpr float kStormHaze = 2.0f;
    constexpr float kStormDimming = 0.5f;

    // The dust clean air carries anyway, as a fraction of the haze a clear day has, under whatever
    // the slider adds. Without any, air scatters sunlight forward and back alike, so a low sun lights
    // the far half of the sky as brightly as its own: three degrees after sunrise the far horizon was
    // at seven tenths of the sun's side, and this floor brings it to a third. Near a high sun the sky
    // comes out a fifth brighter, and the zenith does not change.
    constexpr float kCleanAirHaze = 0.3f;

    constexpr float kDegrees = 57.29578f;

    // What the sun is worth in the shader before the slider scales it. Set so that a clear noon sky
    // reads right with the slider at its default rather than pinned at the top of its range.
    constexpr float kSunIntensity = 44.0f;

    bool g_enabled = false;
    float g_haze = 1.0f;
    float g_brightness = 1.0f;
    float g_gradient = 1.0f;

    // What went into the model last and what came out of it, kept for the heartbeat. After dark
    // these are the numbers that say whether the sky is dark because the air really is unlit or
    // because the model has nothing left to stand on.
    float g_lastNight = 0.0f;
    float g_lastStorm = 0.0f;
    float g_lastExposure = 0.0f;
    float g_lastHorizon[3] = {0.0f, 0.0f, 0.0f};
    float g_lastAway[3] = {0.0f, 0.0f, 0.0f};
    // Where the sun was and what the engine's own fog ends were, beside what the model made of the
    // same moment - so a horizon that looks wrong can be put down to the air or to the engine.
    float g_lastSunElevation = 0.0f;
    float g_lastFogHeadingOffset = -1.0f;
    float g_lastFogToward[3] = {0.0f, 0.0f, 0.0f};
    float g_lastFogAway[3] = {0.0f, 0.0f, 0.0f};

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DVertexShader9* g_vertexShader = nullptr;
    IDirect3DPixelShader9* g_pixelShader = nullptr;

    // Latched after a failure, so a device that cannot compile the pair is told once rather than
    // asked at every dome.
    bool g_refused = false;

    LARGE_INTEGER g_tickFrequency = {};
    LARGE_INTEGER g_lastTick = {};
    float g_sinceHeartbeat = 0.0f;

    float PassSeconds() {
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        if (g_tickFrequency.QuadPart == 0 || g_lastTick.QuadPart == 0) {
            g_lastTick = now;
            return 0.0f;
        }
        const float seconds = static_cast<float>(now.QuadPart - g_lastTick.QuadPart) /
                              static_cast<float>(g_tickFrequency.QuadPart);
        g_lastTick = now;
        return seconds < 0.0f ? 0.0f : seconds;
    }

    bool EnsureDeviceObjects(IDirect3DDevice9* device) {
        if (g_owner != device) {
            SkyOverhaul::Release(g_vertexShader);
            SkyOverhaul::Release(g_pixelShader);
            g_owner = device;
            g_refused = false;
        }
        if (g_vertexShader != nullptr && g_pixelShader != nullptr) {
            return true;
        }
        if (g_refused) {
            return false;
        }

        const HRESULT vertex = SkyOverhaul::CreateShader(device, g_skyVertexShader, &g_vertexShader);
        const HRESULT pixel = SkyOverhaul::CreateShader(device, g_skyPixelShader, &g_pixelShader);
        if (FAILED(vertex) || FAILED(pixel)) {
            SkyOverhaul::Release(g_vertexShader);
            SkyOverhaul::Release(g_pixelShader);
            g_refused = true;
            SkyOverhaul::Logf("sky: this device refused the shader pair (0x%08X, 0x%08X), so the "
                              "engine keeps its dome",
                              vertex, pixel);
            return false;
        }
        return true;
    }

    // Runs from inside the dome's own draw call, with everything the engine set for it still bound.
    // Nothing here may change a render state, a sampler or a texture: the sun, the moon and the
    // stars are drawn after this with no blend or depth mode of their own, and inherit whatever the
    // dome left behind.
    bool Draw(IDirect3DDevice9* device) {
        SkyOverhaul::Camera::View view;
        SkyOverhaul::CloudLayer::Lighting lighting;
        if (!SkyOverhaul::Camera::Read(device, view) ||
            !SkyOverhaul::CloudLayer::Latest(lighting) || !EnsureDeviceObjects(device)) {
            return false;
        }

        // The weather is folded in here rather than in the shader, so that what crosses into it is
        // one finished number for the air and one for the light.
        const float haze = (kCleanAirHaze + g_haze) * (1.0f + lighting.storm * kStormHaze);
        const float intensity =
            kSunIntensity * g_brightness * (1.0f - lighting.storm * kStormDimming);

        // What our air comes to at the horizon, along the engine's own fog heading and against it,
        // which are the two ends of the ramp it colours its fog by. Handing those over is what
        // makes the land meet the sky: both then fade into the same thing, and neither has to know
        // about the other. Before the exposure, as the engine's own fog colour is.
        float length = std::sqrt(view.fogColourVector[0] * view.fogColourVector[0] +
                                 view.fogColourVector[1] * view.fogColourVector[1]);
        if (length < 0.0001f) {
            length = 1.0f;
        }
        const float toward[3] = {view.fogColourVector[0] / length, view.fogColourVector[1] / length,
                                 0.0f};
        const float away[3] = {-toward[0], -toward[1], 0.0f};
        float towardColour[3];
        float awayColour[3];
        SkyOverhaul::SkyModel::Radiance(toward, lighting.sunDirection, view.eye[2], haze, intensity,
                                        towardColour);
        SkyOverhaul::SkyModel::Radiance(away, lighting.sunDirection, view.eye[2], haze, intensity,
                                        awayColour);
        SkyOverhaul::FogTint::SetHorizon(towardColour, awayColour);

        g_lastNight = lighting.night;
        g_lastStorm = lighting.storm;
        g_lastExposure = view.bloom;
        for (size_t i = 0; i < 3; i++) {
            g_lastHorizon[i] = towardColour[i];
            g_lastAway[i] = awayColour[i];
            g_lastFogToward[i] = view.fogColour[i];
            g_lastFogAway[i] = view.fogColour[i] + view.fogColourRange[i];
        }
        const float sunUp = lighting.sunDirection[2];
        g_lastSunElevation = std::asin(sunUp < -1.0f ? -1.0f : (sunUp > 1.0f ? 1.0f : sunUp)) * kDegrees;
        const float sunAcross = std::sqrt(lighting.sunDirection[0] * lighting.sunDirection[0] +
                                          lighting.sunDirection[1] * lighting.sunDirection[1]);
        if (sunAcross > 0.0001f) {
            float cosine = (toward[0] * lighting.sunDirection[0] + toward[1] * lighting.sunDirection[1]) /
                           sunAcross;
            cosine = cosine < -1.0f ? -1.0f : (cosine > 1.0f ? 1.0f : cosine);
            g_lastFogHeadingOffset = std::acos(cosine) * kDegrees;
        } else {
            g_lastFogHeadingOffset = -1.0f;
        }

        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            lighting.sunDirection[0], lighting.sunDirection[1], lighting.sunDirection[2],
            lighting.night,
            haze, intensity, g_gradient, 0.0f,
            view.fogColour[0], view.fogColour[1], view.fogColour[2], 0.0f,
            view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2], 0.0f,
            view.fogColourVector[0], view.fogColourVector[1], 0.0f, 0.0f};

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kConstantCount);
        device->SetVertexShader(g_vertexShader);
        device->SetPixelShader(g_pixelShader);
        device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
        return guard.ClipQuad(kSkyDepth, view.corners);
    }
}

bool SkyOverhaul::Sky::Install() {
    QueryPerformanceFrequency(&g_tickFrequency);
    // The world's own fog colour is followed whether or not our sky is drawn: it is what the land
    // fades into, and a player who wants the horizon changed wants the land changed with it.
    FogTint::Install();
    return DomeDraw::Install(&Draw);
}

void SkyOverhaul::Sky::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    g_sinceHeartbeat += PassSeconds();
    if (g_sinceHeartbeat < kHeartbeatSeconds) {
        return;
    }
    g_sinceHeartbeat = 0.0f;
    // Counts that stand still are the two ways this fails without anything else saying so: a dome
    // that stopped being recognised, and a fog colour that is never being reached.
    Logf("sky f%u: %u domes replaced, %u fog uploads retinted | night %.2f storm %.2f exposure %.2f",
         pass.frame, DomeDraw::SubstituteCount(), FogTint::TintCount(), g_lastNight, g_lastStorm,
         g_lastExposure);
    // The fog heading is the direction the engine's fog ramp starts from, measured against the sun:
    // near zero means the ramp's first colour is the sun's side, as its name says.
    Logf("sky f%u: sun %+.1f deg, fog heading %.0f deg off it | model toward (%.3f %.3f %.3f) "
         "away (%.3f %.3f %.3f)",
         pass.frame, g_lastSunElevation, g_lastFogHeadingOffset, g_lastHorizon[0], g_lastHorizon[1],
         g_lastHorizon[2], g_lastAway[0], g_lastAway[1], g_lastAway[2]);
    Logf("sky f%u: engine fog toward (%.3f %.3f %.3f) away (%.3f %.3f %.3f)", pass.frame,
         g_lastFogToward[0], g_lastFogToward[1], g_lastFogToward[2], g_lastFogAway[0],
         g_lastFogAway[1], g_lastFogAway[2]);
}

void SkyOverhaul::Sky::ReleaseDeviceObjects() {
    Release(g_vertexShader);
    Release(g_pixelShader);
    g_owner = nullptr;
    DrawGuard::ReleaseDeviceObjects();
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

void SkyOverhaul::Sky::SetHaze(int percent) {
    // Forty is a clear day, which is where the slider sits by default.
    g_haze = static_cast<float>(percent) * 0.025f;
}

void SkyOverhaul::Sky::SetBrightness(int percent) {
    g_brightness = static_cast<float>(percent) * 0.01f;
}

void SkyOverhaul::Sky::SetHorizonGradient(int percent) {
    g_gradient = static_cast<float>(percent) * 0.01f;
}

