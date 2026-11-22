#include "ripples.h"

#include "engine/clock.h"
#include "engine/noise.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"

#include "ripples_ps.h"

#include <cmath>

namespace {
    constexpr UINT kFirstConstant = 91;
    constexpr UINT kConstants = 2;
    constexpr DWORD kShapeSampler = 0;

    constexpr UINT kSize = 512;
    // Ring cells across the map, and how steep a ring's crest is in drizzle and in a downpour.
    constexpr float kCells = 14.0f;
    constexpr float kGentlest = 4.0f;
    constexpr float kSteepest = 9.0f;
    // The rain's clock wraps every this many seconds.
    constexpr double kClockWrap = 4096.0;
    constexpr float kFaintest = 0.01f;

    SkyOverhaul::PixelShader g_shader{"ripples", g_ripplesPixelShader};
    SkyOverhaul::OwnedTarget g_map{"ripples", D3DFMT_A8R8G8B8};
    SkyOverhaul::Stopwatch g_stopwatch;
    double g_seconds = 0.0;
    bool g_drawn = false;
}

void SkyOverhaul::Ripples::OnScenePass(const Frame::Pass& pass, float rain) {
    g_seconds = std::fmod(g_seconds + g_stopwatch.Lap(), kClockWrap);
    g_drawn = false;
    IDirect3DDevice9* device = pass.device;
    IDirect3DPixelShader9* shader = g_shader.Get(device);
    if (rain < kFaintest || shader == nullptr || !Noise::Ensure(device) ||
        !g_map.Ensure(device, kSize, kSize)) {
        return;
    }
    ScreenDraw draw(device, kFirstConstant, kConstants);
    const float constants[kConstants * 4] = {
        rain, static_cast<float>(g_seconds), kCells, kGentlest + (kSteepest - kGentlest) * rain,
        1.0f / static_cast<float>(kSize), 1.0f / static_cast<float>(kSize), 0.0f, 0.0f};
    device->SetPixelShaderConstantF(kFirstConstant, constants, kConstants);
    device->SetTexture(kShapeSampler, Noise::Shape());
    for (D3DSAMPLERSTATETYPE axis : {D3DSAMP_ADDRESSU, D3DSAMP_ADDRESSV, D3DSAMP_ADDRESSW}) {
        device->SetSamplerState(kShapeSampler, axis, D3DTADDRESS_WRAP);
    }
    device->SetSamplerState(kShapeSampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(kShapeSampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    AwayFromPass away(pass);
    device->SetRenderTarget(0, g_map.surface);
    device->SetPixelShader(shader);
    draw.ClipQuad(0.0f);
    g_drawn = true;
}

IDirect3DTexture9* SkyOverhaul::Ripples::Texture() {
    return g_drawn ? g_map.texture : nullptr;
}

void SkyOverhaul::Ripples::ReleaseDeviceObjects() {
    g_shader.Release();
    g_map.Release();
    g_drawn = false;
}
