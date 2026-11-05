// A picture copied as it is into a target of its own.
#pragma once

#include "engine/render_target.h"

#include <d3d9.h>

namespace AimingOverhaul {

class PlainCopy {
public:
    // `name` is what the log calls it when the device refuses its target.
    explicit PlainCopy(const char* name) : m_name(name) {}

    // `source` copied, or null where the device will not.
    IDirect3DTexture9* Take(IDirect3DDevice9* device, IDirect3DSurface9* source);

    // The copy last taken, or null.
    IDirect3DTexture9* Texture() const { return m_copy.texture; }

    void Release();

private:
    bool Ensure(IDirect3DDevice9* device, const D3DSURFACE_DESC& desc);

    const char* m_name;
    // The target, on the device that made it, refused until a reset if it would not.
    IDirect3DDevice9* m_owner = nullptr;
    Target m_copy;
    D3DSURFACE_DESC m_desc = {};
    bool m_refused = false;
};

// The finished frame's copy, shared by the composites, each done with it within its own callback.
PlainCopy& FrameCopy();

}
