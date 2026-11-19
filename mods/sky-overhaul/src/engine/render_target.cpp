#include "engine/render_target.h"

#include "engine/com.h"
#include "fcse_api.h"

bool SkyOverhaul::OwnedTarget::Ensure(IDirect3DDevice9* device, UINT width, UINT height) {
    if (m_refused) {
        return false;
    }
    if (m_device == device && texture != nullptr && m_width == width && m_height == height) {
        return true;
    }
    Release();
    m_device = device;
    D3DSURFACE_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.Format = m_format;
    const HRESULT created = CreateTarget(device, desc, &texture, &surface);
    if (FAILED(created)) {
        Release();
        m_refused = true;
        FCSE::Logf("%s: no %ux%u target, 0x%08lX", m_owner, width, height,
                   static_cast<unsigned long>(created));
        return false;
    }
    m_width = width;
    m_height = height;
    return true;
}

void SkyOverhaul::OwnedTarget::Release() {
    SkyOverhaul::Release(surface);
    SkyOverhaul::Release(texture);
    m_device = nullptr;
    m_width = 0;
    m_height = 0;
    m_refused = false;
}
