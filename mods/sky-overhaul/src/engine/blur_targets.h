// Two render targets of one size that a separable blur passes between.
#pragma once

#include "engine/render_target.h"
#include "engine/screen_draw.h"

#include <d3d9.h>

namespace SkyOverhaul {

class BlurTargets {
public:
    // `owner` names the effect in the log.
    explicit BlurTargets(const char* owner)
        : m_targets{{owner, D3DFMT_A8R8G8B8}, {owner, D3DFMT_A8R8G8B8}} {}

    // Both targets at `width` by `height`, made on first use. False once the device has refused
    // one, which it logs; Release clears that.
    bool Ensure(IDirect3DDevice9* device, UINT width, UINT height);

    void Release();

    // Blurs the first target across and then down, back into itself, through the bound pixel
    // shader: each pass reads s0 and finds its axis, one texel long, in xy of `axisRegister`, and
    // one texel in texture coordinates in its zw.
    void Blur(IDirect3DDevice9* device, DrawGuard& draw, UINT axisRegister);

    const OwnedTarget& First() const { return m_targets[0]; }

private:
    OwnedTarget m_targets[2];
};

}
