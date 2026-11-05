// A picture copied as it is. See plain_copy.h.
#include "engine/plain_copy.h"

#include "fcse_api.h"

bool AimingOverhaul::PlainCopy::Ensure(IDirect3DDevice9* device, const D3DSURFACE_DESC& desc) {
    if (m_owner == device && m_copy.texture != nullptr && m_desc.Width == desc.Width &&
        m_desc.Height == desc.Height && m_desc.Format == desc.Format) {
        return true;
    }
    AimingOverhaul::Release(m_copy);
    if (m_refused) {
        return false;
    }
    m_owner = device;
    m_desc = desc;
    if (FAILED(CreateTarget(device, desc.Width, desc.Height, desc.Format, m_copy))) {
        AimingOverhaul::Release(m_copy);
        m_refused = true;
        FCSE::Logf("%s: the device refused a %ux%u copy of format %d", m_name, desc.Width,
                   desc.Height, desc.Format);
        return false;
    }
    return true;
}

IDirect3DTexture9* AimingOverhaul::PlainCopy::Take(IDirect3DDevice9* device,
                                                   IDirect3DSurface9* source) {
    D3DSURFACE_DESC desc = {};
    source->GetDesc(&desc);
    return Ensure(device, desc) && SUCCEEDED(device->StretchRect(source, nullptr, m_copy.surface,
                                                                 nullptr, D3DTEXF_NONE))
               ? m_copy.texture
               : nullptr;
}

void AimingOverhaul::PlainCopy::Release() {
    AimingOverhaul::Release(m_copy);
    m_owner = nullptr;
    m_refused = false;
}

AimingOverhaul::PlainCopy& AimingOverhaul::FrameCopy() {
    static PlainCopy copy{"frame copy"};
    return copy;
}
