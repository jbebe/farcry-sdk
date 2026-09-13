// A texture of our own that a draw can render into and a shader can read.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul {

// One single-sampled level at `desc`'s size and format, in the default pool.
inline HRESULT CreateTarget(IDirect3DDevice9* device, const D3DSURFACE_DESC& desc,
                            IDirect3DTexture9** texture, IDirect3DSurface9** surface) {
    const HRESULT created = device->CreateTexture(desc.Width, desc.Height, 1, D3DUSAGE_RENDERTARGET,
                                                  desc.Format, D3DPOOL_DEFAULT, texture, nullptr);
    if (FAILED(created)) {
        return created;
    }
    return *texture == nullptr ? E_FAIL : (*texture)->GetSurfaceLevel(0, surface);
}

}
