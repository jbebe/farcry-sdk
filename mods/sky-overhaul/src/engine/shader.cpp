#include "engine/shader.h"

#include "engine/com.h"
#include "fcse_api.h"

#include <cstring>
#include <vector>

namespace {
    // fxc writes a blob as bytes and the device wants whole tokens, so it is copied into an aligned
    // buffer on the way through.
    std::vector<DWORD> Tokens(const BYTE* blob, size_t size) {
        std::vector<DWORD> tokens(size / sizeof(DWORD));
        std::memcpy(tokens.data(), blob, tokens.size() * sizeof(DWORD));
        return tokens;
    }

    HRESULT Create(IDirect3DDevice9* device, const DWORD* tokens, IDirect3DPixelShader9** out) {
        return device->CreatePixelShader(tokens, out);
    }

    HRESULT Create(IDirect3DDevice9* device, const DWORD* tokens, IDirect3DVertexShader9** out) {
        return device->CreateVertexShader(tokens, out);
    }
}

template <class Interface>
Interface* SkyOverhaul::Shader<Interface>::Get(IDirect3DDevice9* device) {
    if (m_owner != device) {
        Release();
        m_owner = device;
    }
    if (m_shader != nullptr || m_refused) {
        return m_shader;
    }

    const HRESULT created = Create(device, Tokens(m_blob, m_size).data(), &m_shader);
    if (FAILED(created)) {
        SkyOverhaul::Release(m_shader);
        m_refused = true;
        FCSE::Logf("%s: this device refused the shader (0x%08lX)", m_name,
                   static_cast<unsigned long>(created));
    }
    return m_shader;
}

template <class Interface>
void SkyOverhaul::Shader<Interface>::Release() {
    SkyOverhaul::Release(m_shader);
    m_owner = nullptr;
    m_refused = false;
}

template class SkyOverhaul::Shader<IDirect3DPixelShader9>;
template class SkyOverhaul::Shader<IDirect3DVertexShader9>;
