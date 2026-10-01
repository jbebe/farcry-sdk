#include "engine/frame_copy.h"

#include "engine/com.h"
#include "engine/render_target.h"

namespace {
    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DTexture9* g_copy = nullptr;
    IDirect3DSurface9* g_surface = nullptr;
    D3DSURFACE_DESC g_desc = {};

    void ReleaseCopy() {
        SkyOverhaul::Release(g_surface);
        SkyOverhaul::Release(g_copy);
    }

    bool Ensure(IDirect3DDevice9* device, const D3DSURFACE_DESC& backBuffer) {
        if (g_owner != device) {
            ReleaseCopy();
            g_owner = device;
        }
        if (g_copy != nullptr && g_desc.Width == backBuffer.Width &&
            g_desc.Height == backBuffer.Height && g_desc.Format == backBuffer.Format) {
            return true;
        }
        ReleaseCopy();
        if (FAILED(SkyOverhaul::CreateTarget(device, backBuffer, &g_copy, &g_surface))) {
            ReleaseCopy();
            return false;
        }
        g_desc = backBuffer;
        return true;
    }
}

IDirect3DTexture9* SkyOverhaul::FrameCopy::Take(const Frame::Pass& pass) {
    if (!Ensure(pass.device, pass.backBuffer) ||
        FAILED(pass.device->StretchRect(pass.target, nullptr, g_surface, nullptr, D3DTEXF_NONE))) {
        return nullptr;
    }
    return g_copy;
}

void SkyOverhaul::FrameCopy::ReleaseDeviceObjects() {
    ReleaseCopy();
    g_owner = nullptr;
}
