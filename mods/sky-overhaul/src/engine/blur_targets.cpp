#include "engine/blur_targets.h"

bool SkyOverhaul::BlurTargets::Ensure(IDirect3DDevice9* device, UINT width, UINT height) {
    return m_targets[0].Ensure(device, width, height) && m_targets[1].Ensure(device, width, height);
}

void SkyOverhaul::BlurTargets::Release() {
    for (OwnedTarget& target : m_targets) {
        target.Release();
    }
}

void SkyOverhaul::BlurTargets::Blur(IDirect3DDevice9* device, DrawGuard& draw, UINT axisRegister) {
    const float axes[2][2] = {{1.0f, 0.0f}, {0.0f, 1.0f}};
    const float texelX = 1.0f / static_cast<float>(m_targets[0].Width());
    const float texelY = 1.0f / static_cast<float>(m_targets[0].Height());
    for (int pass = 0; pass < 2; pass++) {
        const OwnedTarget& from = m_targets[pass];
        const OwnedTarget& into = m_targets[1 - pass];
        const float axis[4] = {axes[pass][0], axes[pass][1], texelX, texelY};
        device->SetPixelShaderConstantF(axisRegister, axis, 1);
        // Before the target changes, so `into` is never bound to be read and written at once.
        device->SetTexture(0, from.texture);
        device->SetRenderTarget(0, into.surface);
        draw.ClipQuad(0.0f);
    }
}
