#include "engine/blur_targets.h"

#include "engine/com.h"
#include "engine/render_target.h"
#include "fcse_api.h"

bool SkyOverhaul::BlurTargets::Ensure(IDirect3DDevice9* device, UINT width, UINT height) {
    if (m_refused) {
        return false;
    }
    if (m_device == device && m_targets[0].texture != nullptr && m_width == width &&
        m_height == height) {
        return true;
    }
    Release();
    m_device = device;
    D3DSURFACE_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.Format = D3DFMT_A8R8G8B8;
    for (Target& target : m_targets) {
        const HRESULT created = CreateTarget(device, desc, &target.texture, &target.surface);
        if (FAILED(created)) {
            Release();
            m_refused = true;
            FCSE::Logf("%s: no %ux%u target, 0x%08lX", m_owner, width, height,
                       static_cast<unsigned long>(created));
            return false;
        }
    }
    m_width = width;
    m_height = height;
    return true;
}

void SkyOverhaul::BlurTargets::Release() {
    for (Target& target : m_targets) {
        SkyOverhaul::Release(target.surface);
        SkyOverhaul::Release(target.texture);
    }
    m_device = nullptr;
    m_width = 0;
    m_height = 0;
    m_refused = false;
}

void SkyOverhaul::BlurTargets::Blur(IDirect3DDevice9* device, DrawGuard& draw, UINT axisRegister) {
    const float axes[2][2] = {{1.0f, 0.0f}, {0.0f, 1.0f}};
    const float texelX = 1.0f / static_cast<float>(m_width);
    const float texelY = 1.0f / static_cast<float>(m_height);
    for (int pass = 0; pass < 2; pass++) {
        const Target& from = m_targets[pass];
        const Target& into = m_targets[1 - pass];
        const float axis[4] = {axes[pass][0], axes[pass][1], texelX, texelY};
        device->SetPixelShaderConstantF(axisRegister, axis, 1);
        // Before the target changes, so `into` is never bound to be read and written at once.
        device->SetTexture(0, from.texture);
        device->SetRenderTarget(0, into.surface);
        draw.ClipQuad(0.0f);
    }
}
