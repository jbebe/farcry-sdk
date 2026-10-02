// Textures of our own that a draw can render into and a shader can read.
#pragma once

#include "engine/com.h"

#include <d3d9.h>

namespace WeaponOverhaul {

struct Target {
    IDirect3DTexture9* texture = nullptr;
    IDirect3DSurface9* surface = nullptr;
};

// One single-sampled level in the default pool. A depth texture passes D3DUSAGE_DEPTHSTENCIL.
inline HRESULT CreateTarget(IDirect3DDevice9* device, UINT width, UINT height, D3DFORMAT format,
                            Target& target, DWORD usage = D3DUSAGE_RENDERTARGET) {
    HRESULT created = device->CreateTexture(width, height, 1, usage, format, D3DPOOL_DEFAULT,
                                            &target.texture, nullptr);
    if (SUCCEEDED(created)) {
        created = target.texture->GetSurfaceLevel(0, &target.surface);
    }
    return created;
}

inline void Release(Target& target) {
    Release(target.surface);
    Release(target.texture);
}

}
