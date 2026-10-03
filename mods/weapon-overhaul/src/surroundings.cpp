#include "surroundings.h"

#include "engine/aim.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/second_view.h"
#include "engine/shader.h"
#include "eyepiece.h"
#include "scope_lens.h"

#include "surroundings_ps.h"

#include <atomic>

namespace {
    WeaponOverhaul::PixelShader g_shader{"surroundings", g_surroundingsPixelShader};

    std::atomic<bool> g_enabled{true};
    // The camera's field of view the last time the player was not aiming, in radians.
    float g_ownFov = 0.0f;
}

float WeaponOverhaul::Surroundings::Want(float cameraFov) {
    if (Aim::Settled() <= 0.0f && Aim::Scoped() <= 0.0f && !Aim::ScopeUp()) {
        g_ownFov = cameraFov;
    }
    return g_enabled && Eyepiece::Active() ? g_ownFov : 0.0f;
}

void WeaponOverhaul::Surroundings::BeforeColour(IDirect3DDevice9* device) {
    IDirect3DTexture9* view = SecondView::Latest();
    IDirect3DTexture9* lens = ScopeLens::Found();
    IDirect3DPixelShader9* shader = view != nullptr && lens != nullptr && g_enabled &&
                                            Eyepiece::Active()
                                        ? g_shader.Get(device)
                                        : nullptr;
    if (shader == nullptr) {
        return;
    }
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float constants[4] = {width / height, ScopeLens::kSmallest, 0.0f, 0.0f};

    ScreenDraw draw(device, 0, 1);
    device->SetPixelShaderConstantF(0, constants, 1);
    device->SetTexture(0, view);
    device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    device->SetTexture(5, lens);
    // The scene's alpha carries brightness for the bloom, so it is left alone.
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShader(shader);
    draw.Quad(0.0f, 0.0f, width, height);
}

void WeaponOverhaul::Surroundings::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Surroundings::ReleaseDeviceObjects() {
    g_shader.Release();
}
