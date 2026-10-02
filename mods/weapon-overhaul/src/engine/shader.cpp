#include "engine/shader.h"

#include "engine/com.h"
#include "fcse_api.h"

#include <cstring>
#include <vector>

IDirect3DPixelShader9* WeaponOverhaul::PixelShader::Get(IDirect3DDevice9* device) {
    if (m_owner != device) {
        Release();
        m_owner = device;
    }
    if (m_shader != nullptr || m_refused) {
        return m_shader;
    }

    // fxc writes a blob as bytes and the device wants whole tokens.
    std::vector<DWORD> tokens(m_size / sizeof(DWORD));
    std::memcpy(tokens.data(), m_blob, tokens.size() * sizeof(DWORD));
    const HRESULT created = device->CreatePixelShader(tokens.data(), &m_shader);
    if (FAILED(created)) {
        WeaponOverhaul::Release(m_shader);
        m_refused = true;
        FCSE::Logf("%s: this device refused the shader (0x%08lX)", m_name,
                   static_cast<unsigned long>(created));
    }
    return m_shader;
}

void WeaponOverhaul::PixelShader::Release() {
    WeaponOverhaul::Release(m_shader);
    m_owner = nullptr;
    m_refused = false;
}
