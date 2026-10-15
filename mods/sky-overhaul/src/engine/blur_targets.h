// Two render targets of one size that a separable blur passes between.
#pragma once

#include "engine/screen_draw.h"

#include <d3d9.h>

namespace SkyOverhaul {

class BlurTargets {
public:
    struct Target {
        IDirect3DTexture9* texture;
        IDirect3DSurface9* surface;
    };

    // `owner` names the effect in the log.
    explicit BlurTargets(const char* owner) : m_owner(owner) {}

    // Both targets at `width` by `height`, made on first use. False once the device has refused
    // one, which it logs; Release clears that.
    bool Ensure(IDirect3DDevice9* device, UINT width, UINT height);

    void Release();

    // Blurs the first target across and then down, back into itself, through the bound pixel
    // shader: each pass reads s0 and finds its axis, one texel long, in xy of `axisRegister`, with
    // `zw` in its zw.
    void Blur(IDirect3DDevice9* device, DrawGuard& draw, UINT axisRegister, float z, float w);

    const Target& First() const { return m_targets[0]; }
    UINT Width() const { return m_width; }
    UINT Height() const { return m_height; }

private:
    const char* m_owner;
    IDirect3DDevice9* m_device = nullptr;
    Target m_targets[2] = {};
    UINT m_width = 0;
    UINT m_height = 0;
    bool m_refused = false;
};

}
