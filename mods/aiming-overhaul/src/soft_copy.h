// A picture shrunk to half its size and blurred across and down, in targets of its own.
#pragma once

#include "engine/render_target.h"
#include "engine/shader.h"

#include <d3d9.h>

namespace AimingOverhaul {

class SoftCopy {
public:
    // `name` is what the log calls it when the device refuses its targets.
    explicit SoftCopy(const char* name);

    // `source` at half its size, blurred with a sigma of `sigma` screen heights, or null where the
    // device will not; the device's state is left as it was.
    IDirect3DTexture9* Soften(IDirect3DDevice9* device, IDirect3DSurface9* source, float sigma);

    void Release();

private:
    bool Ensure(IDirect3DDevice9* device, IDirect3DSurface9* source);

    const char* m_name;
    PixelShader m_blur;
    // The targets, on the device that made them, refused until a reset if it would not.
    IDirect3DDevice9* m_owner = nullptr;
    Target m_soft;
    Target m_across;
    UINT m_width = 0;
    UINT m_height = 0;
    D3DFORMAT m_format = D3DFMT_UNKNOWN;
    bool m_refused = false;
};

}
