// A clear disc a little wider than the lens, swung off its centre as the look turns, leaves the rest
// of the lens dark.
#include "scope_shadow.h"

#include "engine/aim.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "scope_lens.h"

#include "scope_shadow_ps.h"

#include <atomic>

namespace {
    // The shadow is the rim of the magnified tube, a circle a little wider than the lens whose soft
    // edge just reaches the lens's while it is centred. In lens radii, then how dark it gets.
    constexpr float kShadowRadius = 1.2f;
    constexpr float kSoftEdge = 0.2f;
    constexpr float kDarkness = 0.95f;

    constexpr UINT kConstantCount = 3;

    WeaponOverhaul::PixelShader g_shadowShader{"scope shadow", g_scopeShadowPixelShader};

    std::atomic<bool> g_enabled{true};
}

void WeaponOverhaul::ScopeShadow::OnGunPass(const Frame::Pass& pass,
                                            const WeaponDraws::Depth& depth, float hole) {
    const float scoped = g_enabled ? Aim::Scoped() : 0.0f;
    IDirect3DTexture9* lens = ScopeLens::Found();
    IDirect3DPixelShader9* shadow = scoped > 0.0f ? g_shadowShader.Get(pass.device) : nullptr;
    if (lens == nullptr || shadow == nullptr) {
        return;
    }

    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const Aim::Swing swing = Aim::ScopeSwing();
    const float constants[kConstantCount * 4] = {
        swing.x, swing.y, scoped, width / height,
        kSoftEdge, kDarkness, ScopeLens::kSmallest, kShadowRadius,
        hole, 0.0f, 0.0f, 0.0f,
    };

    IDirect3DDevice9* device = pass.device;
    ScreenDraw draw(device, 0, kConstantCount);
    device->SetPixelShaderConstantF(0, constants, kConstantCount);
    device->SetTexture(3, depth.texture);
    device->SetTexture(5, lens);
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShader(shadow);
    draw.Quad(0.0f, 0.0f, width, height);
}

void WeaponOverhaul::ScopeShadow::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::ScopeShadow::ReleaseDeviceObjects() {
    g_shadowShader.Release();
}
