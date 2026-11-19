// A texture of our own that a draw can render into and a shader can read.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul {

// One single-sampled level at `desc`'s size and format, in the default pool. A depth texture passes
// D3DUSAGE_DEPTHSTENCIL.
inline HRESULT CreateTarget(IDirect3DDevice9* device, const D3DSURFACE_DESC& desc,
                            IDirect3DTexture9** texture, IDirect3DSurface9** surface,
                            DWORD usage = D3DUSAGE_RENDERTARGET) {
    const HRESULT created = device->CreateTexture(desc.Width, desc.Height, 1, usage, desc.Format,
                                                  D3DPOOL_DEFAULT, texture, nullptr);
    if (FAILED(created)) {
        return created;
    }
    return *texture == nullptr ? E_FAIL : (*texture)->GetSurfaceLevel(0, surface);
}

// One such target in a fixed format, made on first use and again when the device or the size
// changes.
class OwnedTarget {
public:
    // `owner` names the effect in the log.
    OwnedTarget(const char* owner, D3DFORMAT format) : m_owner(owner), m_format(format) {}

    // False once the device has refused it, which it logs; Release clears that.
    bool Ensure(IDirect3DDevice9* device, UINT width, UINT height);

    void Release();

    UINT Width() const { return m_width; }
    UINT Height() const { return m_height; }

    IDirect3DTexture9* texture = nullptr;
    IDirect3DSurface9* surface = nullptr;

private:
    const char* m_owner;
    D3DFORMAT m_format;
    IDirect3DDevice9* m_device = nullptr;
    UINT m_width = 0;
    UINT m_height = 0;
    bool m_refused = false;
};

}
