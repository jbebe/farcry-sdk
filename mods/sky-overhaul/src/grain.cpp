#include "grain.h"

#include "engine/clock.h"
#include "engine/frame_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"

#include "grain_ps.h"

namespace {
    constexpr UINT kConstantCount = 2;

    // The grain at full strength, from its darkest to its brightest at mid grey.
    constexpr float kFullGrain = 0.25f;

    // Below this the grain changes nothing a player could see.
    constexpr float kFaintest = 0.002f;

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"grain", g_grainPixelShader};
    SkyOverhaul::GrainClock g_grain;
}

void SkyOverhaul::Grain::OnFinalPass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.live) {
        return;
    }
    g_grain.Advance();
    const Tuning::Values v = Tuning::Current();
    const float strength = v.grainStrength * kFullGrain;
    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    if (strength < kFaintest || shader == nullptr) {
        return;
    }
    IDirect3DTexture9* scene = FrameCopy::Take(pass);
    if (scene == nullptr) {
        return;
    }

    const float width = static_cast<float>(pass.backBuffer.Width);
    const float height = static_cast<float>(pass.backBuffer.Height);
    const float constants[kConstantCount * 4] = {
        width, height, 1.0f / v.grainSize, g_grain.Pattern(),
        strength, v.grainColour, 0.0f, 0.0f};

    ScreenDraw draw(pass.device, 0, kConstantCount);
    pass.device->SetPixelShader(shader);
    pass.device->SetTexture(0, scene);
    pass.device->SetPixelShaderConstantF(0, constants, kConstantCount);
    draw.Quad(0.0f, 0.0f, width, height);
}

void SkyOverhaul::Grain::ReleaseDeviceObjects() {
    g_shader.Release();
}

void SkyOverhaul::Grain::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
