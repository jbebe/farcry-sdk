// CGadgetMapStrategy::SetMapTexture gives the map entity's "maptop" material the texture of the
// area the player is in. Wherever that texture is bound while the notebook's face is up, the page
// is bound in its place, so the page is drawn on the paper, lit and moved as the paper is.
#include "engine/paper.h"

#include "engine/direct3d.h"
#include "engine/memory.h"
#include "fcse_api.h"
#include "notebook.h"

#include <atomic>
#include <cstring>
#include <d3d9.h>
#include <mutex>
#include <vector>

namespace InventoryOverhaul::Paper {

namespace {
    using SetMapTextureFn = void(__fastcall*)(void* strategy, void* unused, void* resource,
                                              uint32_t owned);
    using SetTextureFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, DWORD stage,
                                             IDirect3DBaseTexture9* texture);

    // SetMapTexture(CSmartResourcePtr<CTextureResource>), its pointer passed by value with a flag.
    // Not an entry the address library knows.
    FCSE::Relocation<SetMapTextureFn> g_setMapTextureSite{
        FCSE::Pattern("53 8B D9 8B 43 38 55 56 8B 70 08 83 46 08 01 57 8B 7E 0C 8B CF E8 ?? ?? ?? ?? "
                      "83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8")};
    SetMapTextureFn g_setMapTexture = nullptr;
    SetTextureFn g_setTexture = nullptr;

    // CTextureResource: the texture it hands the renderer; that one's device texture, a
    // CTextureD3D9; and that one's Direct3D texture.
    constexpr ptrdiff_t kResourceTexture = 0x28;
    constexpr ptrdiff_t kTextureDevice = 0x0C;
    constexpr ptrdiff_t kDeviceDirect3D = 0x18;

    std::atomic<const uint8_t*> g_paperResource{nullptr};

    // The page waiting to be put into the texture.
    std::mutex g_lock;
    std::vector<uint32_t> g_pixels;
    int g_size = 0;
    bool g_fresh = false;

    // On the render thread: the paper's Direct3D texture and the resource it was found from, and
    // the page's texture.
    const uint8_t* g_paperFrom = nullptr;
    IDirect3DBaseTexture9* g_paper = nullptr;
    IDirect3DTexture9* g_page = nullptr;
    int g_pageSize = 0;

    // Null where the engine's pointers do not hold, as while the texture is still loading.
    IDirect3DBaseTexture9* Direct3DTextureOf(const uint8_t* resource) {
        __try {
            const uint8_t* texture = *reinterpret_cast<uint8_t* const*>(resource + kResourceTexture);
            const uint8_t* device = *reinterpret_cast<uint8_t* const*>(texture + kTextureDevice);
            return *reinterpret_cast<IDirect3DBaseTexture9* const*>(device + kDeviceDirect3D);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return nullptr;
        }
    }

    void __fastcall OnSetMapTexture(void* strategy, void* unused, void* resource, uint32_t owned) {
        g_paperResource = static_cast<const uint8_t*>(resource);
        g_setMapTexture(strategy, unused, resource, owned);
    }

    // The page's texture, made on first use and filled with the latest page; null until a page
    // has been handed over.
    IDirect3DTexture9* Page(IDirect3DDevice9* device) {
        std::lock_guard<std::mutex> held(g_lock);
        if (!g_fresh) {
            return g_page;
        }
        if (g_page != nullptr && g_pageSize != g_size) {
            g_page->Release();
            g_page = nullptr;
        }
        if (g_page == nullptr &&
            FAILED(device->CreateTexture(g_size, g_size, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED,
                                         &g_page, nullptr))) {
            FCSE::Logf("paper: no texture could be made for the page");
            g_fresh = false;
            return nullptr;
        }
        g_pageSize = g_size;
        D3DLOCKED_RECT locked{};
        if (SUCCEEDED(g_page->LockRect(0, &locked, nullptr, 0))) {
            for (int row = 0; row < g_size; ++row) {
                std::memcpy(static_cast<uint8_t*>(locked.pBits) + row * locked.Pitch,
                            g_pixels.data() + row * g_size, g_size * sizeof(uint32_t));
            }
            g_page->UnlockRect(0);
        }
        g_fresh = false;
        return g_page;
    }

    HRESULT __stdcall OnSetTexture(IDirect3DDevice9* device, DWORD stage,
                                   IDirect3DBaseTexture9* texture) {
        if (texture != nullptr && Notebook::IsOpen()) {
            const uint8_t* resource = g_paperResource;
            if (resource != g_paperFrom || g_paper == nullptr) {
                g_paperFrom = resource;
                g_paper = resource != nullptr ? Direct3DTextureOf(resource) : nullptr;
            }
            if (texture == g_paper) {
                if (IDirect3DTexture9* page = Page(device)) {
                    return g_setTexture(device, stage, page);
                }
            }
        }
        return g_setTexture(device, stage, texture);
    }
}

bool Install() {
    const bool hooked = g_setMapTextureSite &&
                        Hook(g_setMapTextureSite, &OnSetMapTexture, &g_setMapTexture) &&
                        Direct3D::Hook(Direct3D::kSetTexture, &OnSetTexture, &g_setTexture);
    FCSE::Logf("paper: %s", hooked ? "the page replaces the map's texture"
                                   : "off, the map's texture or the device was not found");
    return hooked;
}

void Show(const uint32_t* pixels, int size) {
    std::lock_guard<std::mutex> held(g_lock);
    g_pixels.assign(pixels, pixels + size * size);
    g_size = size;
    g_fresh = true;
}

}
