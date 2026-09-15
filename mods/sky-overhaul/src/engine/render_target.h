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

}
