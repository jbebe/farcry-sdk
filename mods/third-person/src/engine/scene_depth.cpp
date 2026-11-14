#include "engine/scene_depth.h"

#include "engine/com.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <atomic>

namespace {
    // The renderer's per-view shader parameter update, __thiscall declared __fastcall: among the
    // viewport's parameters it hands the view's linear depth to DepthVPSampler.
    using UpdateViewFn = void(__fastcall*)(void* renderer, void* unused, uint8_t* view);
    FCSE::Relocation<UpdateViewFn> g_updateView{FCSE::Uplay(0x00367F70)};
    UpdateViewFn g_originalUpdateView = nullptr;

    // A view's linear depth, a device texture; and that one's Direct3D texture.
    constexpr ptrdiff_t kViewDepth = 0x1C;
    constexpr ptrdiff_t kDeviceDirect3D = 0x18;

    // The depth of the last view updated that has one, the world's by the time the world's passes
    // draw.
    std::atomic<uint8_t*> g_depth{nullptr};

    void __fastcall UpdateViewDetour(void* renderer, void* unused, uint8_t* view) {
        if (uint8_t* depth = ThirdPerson::Field<uint8_t*>(view, kViewDepth); depth != nullptr) {
            g_depth = depth;
        }
        g_originalUpdateView(renderer, unused, view);
    }

    // Null where the engine's pointers do not hold.
    IDirect3DBaseTexture9* Direct3DOf(uint8_t* deviceTexture) {
        if (deviceTexture == nullptr) {
            return nullptr;
        }
        __try {
            return ThirdPerson::Field<IDirect3DBaseTexture9*>(deviceTexture, kDeviceDirect3D);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return nullptr;
        }
    }

}

bool ThirdPerson::SceneDepth::Install() {
    if (!Hook(g_updateView.address(), &UpdateViewDetour, &g_originalUpdateView)) {
        FCSE::Logf("scene depth: the renderer's view update cannot be hooked; no shadow is drawn");
        return false;
    }
    return true;
}

IDirect3DTexture9* ThirdPerson::SceneDepth::Texture(UINT width, UINT height) {
    IDirect3DBaseTexture9* base = Direct3DOf(g_depth);
    if (base == nullptr || base->GetType() != D3DRTYPE_TEXTURE) {
        return nullptr;
    }
    auto* texture = static_cast<IDirect3DTexture9*>(base);
    D3DSURFACE_DESC desc = {};
    const bool scene = SUCCEEDED(texture->GetLevelDesc(0, &desc)) && desc.Format == D3DFMT_A8R8G8B8 &&
                       (desc.Usage & D3DUSAGE_RENDERTARGET) != 0 && desc.Width == width &&
                       desc.Height == height;
    return scene ? texture : nullptr;
}
