#include "rainbow.h"

#include "angles.h"
#include "clouds.h"
#include "hash.h"
#include "sky.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"
#include "weather/weather.h"

#include "rainbow_ps.h"

#include <cmath>

namespace {
    // Above the engine's own globals, which occupy c0 to c64.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 4;

    // How bright the bow's brightest colour is against the sunlight that lights the rain.
    constexpr float kBrightness = 0.035f;

    // The bow's radius at its middle colour, as rainbow.fx draws it.
    constexpr float kRadius = SkyOverhaul::Radians(41.5f);

    // How much of its arc over the horizon a bow keeps from one foot, 130 of its 180 degrees, and the
    // seed each new bow rolls its foot from.
    constexpr float kKept = 130.0f / 180.0f;
    constexpr uint32_t kSeed = 0xB0B0u;

    // Real seconds the bow takes to come and go when the weather's own rainbow jumps, as it does when
    // rain picks up again before the last has gone, and how faint a bow is left undrawn.
    constexpr float kEase = 3.0f;
    constexpr float kFaintest = 0.002f;

    SkyOverhaul::PixelShader g_shader{"rainbow", g_rainbowPixelShader};
    SkyOverhaul::Stopwatch g_stopwatch;
    float g_shown = 0.0f;
    // The foot the bow showing is kept from, as -1 or 1.
    float g_foot = 1.0f;
}

void SkyOverhaul::Rainbow::OnScenePass(const Frame::Pass& pass) {
    if (!pass.sky || !pass.live) {
        return;
    }
    // While no bow shows, the next one's foot is rolled from the game's minute.
    if (g_shown < kFaintest) {
        double seconds = 0.0;
        Weather::Clock(seconds);
        g_foot = Random(static_cast<int>(seconds / 60.0), 0, 0, kSeed) < 0.5f ? -1.0f : 1.0f;
    }
    Weather::Reading reading;
    const float wanted = Weather::Latest(reading) ? reading.bow : 0.0f;
    g_shown += (wanted - g_shown) * (1.0f - std::exp(-g_stopwatch.Lap() / kEase));
    CloudLayer::Lighting lighting;
    if (g_shown < kFaintest || !CloudLayer::Latest(lighting)) {
        return;
    }
    // The angle round the ring from its top to where it meets the horizon, none once the sun stands
    // too high for any of it to clear the horizon.
    const float* sun = lighting.sunDirection;
    const float rise = std::tan(std::asin(sun[2])) / std::tan(kRadius);
    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    Camera::View view;
    if (sun[2] <= 0.0f || rise >= 0.99f || shader == nullptr || !Camera::Read(pass.device, view)) {
        return;
    }
    // The sun is behind the eye whenever a bow is in view, so what reaches the eye is measured
    // toward it rather than read from the sun's cover on screen.
    IDirect3DTexture9* sunlit = Clouds::Sunlit(pass, sun);
    if (sunlit == nullptr) {
        return;
    }

    float fill[2];
    Sky::Fill(Tuning::Current(), fill);
    const float constants[kConstantCount * 4] = {
        kBrightness * g_shown * view.bloom, 0.0f, 0.0f, 0.0f,
        sun[0], sun[1], sun[2], 0.0f,
        fill[0], fill[1], 0.0f, 0.0f,
        g_foot, kKept, std::acos(rise), 0.0f};
    // Added over the sky and the clouds where the world drew nothing.
    ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
    pass.device->SetPixelShader(shader);
    pass.device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
    pass.device->SetTexture(0, sunlit);
    pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
    pass.device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    pass.device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
    pass.device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_ONE);
    draw.KeepAlpha();
    draw.ClipQuad(Frame::kFarDepth, view.corners);
}

void SkyOverhaul::Rainbow::ReleaseDeviceObjects() {
    g_shader.Release();
}
