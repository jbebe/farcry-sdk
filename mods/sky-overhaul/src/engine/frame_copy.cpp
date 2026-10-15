#include "engine/frame_copy.h"

#include "engine/com.h"
#include "engine/render_target.h"
#include "fcse_api.h"

namespace {
    // The world's two formats with HDR on, which it alternates between, and the composite's.
    constexpr size_t kCopyCount = 3;

    struct Copy {
        IDirect3DTexture9* texture;
        IDirect3DSurface9* surface;
        D3DSURFACE_DESC desc;
        // Set once the device refuses this format; cleared on reset.
        bool refused;
    };

    IDirect3DDevice9* g_owner = nullptr;
    Copy g_copies[kCopyCount] = {};

    void ReleaseCopies() {
        for (Copy& copy : g_copies) {
            SkyOverhaul::Release(copy.surface);
            SkyOverhaul::Release(copy.texture);
            copy = {};
        }
    }

    // The copy matching `target`, made on first use. Null if the device refused its format.
    Copy* Ensure(IDirect3DDevice9* device, const D3DSURFACE_DESC& target) {
        if (g_owner != device) {
            ReleaseCopies();
            g_owner = device;
        }
        Copy* empty = nullptr;
        for (Copy& copy : g_copies) {
            const bool used = copy.texture != nullptr || copy.refused;
            if (!used) {
                empty = empty != nullptr ? empty : &copy;
            } else if (copy.desc.Width == target.Width && copy.desc.Height == target.Height &&
                       copy.desc.Format == target.Format) {
                return copy.refused ? nullptr : &copy;
            }
        }
        if (empty == nullptr) {
            ReleaseCopies();
            empty = &g_copies[0];
        }

        empty->desc = target;
        const HRESULT created =
            SkyOverhaul::CreateTarget(device, target, &empty->texture, &empty->surface);
        if (FAILED(created)) {
            empty->refused = true;
            FCSE::Logf("frame copy: no %ux%u copy in format %u, 0x%08lX", target.Width,
                       target.Height, static_cast<unsigned>(target.Format),
                       static_cast<unsigned long>(created));
            return nullptr;
        }
        return empty;
    }
}

IDirect3DTexture9* SkyOverhaul::FrameCopy::Take(const Frame::Pass& pass) {
    D3DSURFACE_DESC desc;
    if (FAILED(pass.target->GetDesc(&desc))) {
        return nullptr;
    }
    Copy* copy = Ensure(pass.device, desc);
    if (copy == nullptr) {
        return nullptr;
    }
    const HRESULT copied =
        pass.device->StretchRect(pass.target, nullptr, copy->surface, nullptr, D3DTEXF_NONE);
    if (FAILED(copied)) {
        copy->refused = true;
        FCSE::Logf("frame copy: format %u would not copy, 0x%08lX",
                   static_cast<unsigned>(desc.Format), static_cast<unsigned long>(copied));
        return nullptr;
    }
    return copy->texture;
}

void SkyOverhaul::FrameCopy::ReleaseDeviceObjects() {
    ReleaseCopies();
    g_owner = nullptr;
}
