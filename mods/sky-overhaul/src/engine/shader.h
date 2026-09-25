// Compiled shaders handed to the device.
#pragma once

#include <d3d9.h>

#include <vector>

namespace SkyOverhaul {

// A shader's bytecode as the device hands it back, which is what shadersobj ships; empty if the
// device will not.
template <class Interface>
std::vector<DWORD> Bytecode(Interface* shader) {
    UINT size = 0;
    if (FAILED(shader->GetFunction(nullptr, &size)) || size == 0) {
        return {};
    }
    std::vector<DWORD> tokens(size / sizeof(DWORD));
    if (FAILED(shader->GetFunction(tokens.data(), &size))) {
        return {};
    }
    return tokens;
}

// A shader built into the plugin, created on whichever device asks for it. A device that refuses it
// is logged once and not asked again until the shader is released.
template <class Interface>
class Shader {
public:
    template <size_t N>
    constexpr Shader(const char* name, const BYTE (&blob)[N])
        : m_name(name), m_blob(blob), m_size(N) {
        static_assert(N % sizeof(DWORD) == 0, "a compiled shader is a whole number of tokens");
    }

    // Null while the device refuses it.
    Interface* Get(IDirect3DDevice9* device);

    void Release();

private:
    const char* m_name;
    const BYTE* m_blob;
    size_t m_size;
    IDirect3DDevice9* m_owner = nullptr;
    Interface* m_shader = nullptr;
    bool m_refused = false;
};

using PixelShader = Shader<IDirect3DPixelShader9>;
using VertexShader = Shader<IDirect3DVertexShader9>;

extern template class Shader<IDirect3DPixelShader9>;
extern template class Shader<IDirect3DVertexShader9>;

}
