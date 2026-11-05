// A picture shrunk and blurred. See soft_copy.h.
#include "soft_copy.h"

#include "blur.h"
#include "engine/screen_draw.h"
#include "fcse_api.h"

#include "blur_blur_ps.h"

#include <algorithm>

AimingOverhaul::SoftCopy::SoftCopy(const char* name)
    : m_name(name), m_blur(name, g_blurPixelShader) {}

bool AimingOverhaul::SoftCopy::Ensure(IDirect3DDevice9* device, IDirect3DSurface9* source) {
    D3DSURFACE_DESC desc = {};
    source->GetDesc(&desc);
    const UINT width = (std::max)(desc.Width / 2, 1u);
    const UINT height = (std::max)(desc.Height / 2, 1u);
    if (m_owner == device && m_soft.texture != nullptr && m_width == width && m_height == height &&
        m_format == desc.Format) {
        return true;
    }
    AimingOverhaul::Release(m_soft);
    AimingOverhaul::Release(m_across);
    if (m_refused) {
        return false;
    }
    m_owner = device;
    m_width = width;
    m_height = height;
    m_format = desc.Format;
    if (FAILED(CreateTarget(device, width, height, desc.Format, m_soft)) ||
        FAILED(CreateTarget(device, width, height, desc.Format, m_across))) {
        AimingOverhaul::Release(m_soft);
        AimingOverhaul::Release(m_across);
        m_refused = true;
        FCSE::Logf("%s: the device refused the targets to blur in", m_name);
        return false;
    }
    return true;
}

IDirect3DTexture9* AimingOverhaul::SoftCopy::Soften(IDirect3DDevice9* device,
                                                    IDirect3DSurface9* source, float sigma) {
    IDirect3DPixelShader9* blur = m_blur.Get(device);
    if (blur == nullptr || !Ensure(device, source)) {
        return nullptr;
    }
    ScreenDraw draw(device, Blur::kStep, 1);
    // A target of our own cannot share the scene's multisampled depth.
    device->SetDepthStencilSurface(nullptr);
    device->StretchRect(source, nullptr, m_soft.surface, nullptr, D3DTEXF_LINEAR);
    const float width = static_cast<float>(m_width);
    const float height = static_cast<float>(m_height);
    const float down = sigma / Blur::kSigmaSteps;
    const float across[4] = {down * height / width, 0.0f, 0.0f, 0.0f};
    const float downward[4] = {0.0f, down, 0.0f, 0.0f};
    draw.Linear(0);
    device->SetPixelShader(blur);
    draw.Separable(Blur::kStep, across, downward, m_soft.texture, m_across, m_soft.surface, width,
                   height);
    return m_soft.texture;
}

void AimingOverhaul::SoftCopy::Release() {
    AimingOverhaul::Release(m_soft);
    AimingOverhaul::Release(m_across);
    m_blur.Release();
    m_owner = nullptr;
    m_refused = false;
}
