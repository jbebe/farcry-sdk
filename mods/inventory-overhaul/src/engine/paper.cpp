#include "engine/paper.h"

#include "engine/com.h"
#include "engine/memory.h"
#include "engine/vtable.h"
#include "fcse_api.h"
#include "notebook.h"

#include <atomic>
#include <cstdint>
#include <d3d9.h>

namespace InventoryOverhaul::Paper {

namespace {
    using SetMapTextureFn = void(__fastcall*)(void* strategy, void* unused, void* resource,
                                              uint32_t owned);
    using SetTextureFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, DWORD stage,
                                             IDirect3DBaseTexture9* texture);
    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9* device);
    using ResetFn = HRESULT(__stdcall*)(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters);

    // CGadgetMapStrategy::SetMapTexture(CSmartResourcePtr<CTextureResource>), its pointer passed by
    // value with a flag.
    FCSE::Relocation<SetMapTextureFn> g_setMapTextureSite{
        FCSE::Pattern("53 8B D9 8B 43 38 55 56 8B 70 08 83 46 08 01 57 8B 7E 0C 8B CF E8 ?? ?? ?? ?? "
                      "83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8")};
    SetMapTextureFn g_setMapTexture = nullptr;
    SetTextureFn g_setTexture = nullptr;
    EndSceneFn g_endScene = nullptr;
    ResetFn g_reset = nullptr;

    // CTextureResource: the texture it hands the renderer; that one's device texture, a
    // CTextureD3D9; and that one's Direct3D texture.
    constexpr ptrdiff_t kResourceTexture = 0x28;
    constexpr ptrdiff_t kTextureDevice = 0x0C;
    constexpr ptrdiff_t kDeviceDirect3D = 0x18;

    // The marker images' textures are 4 by 64 and 64 by 4, sizes no other texture has.
    constexpr UINT kMarkShort = 4;
    constexpr UINT kMarkLong = 64;

    enum class Mark : uint8_t { None, Begin, End };

    // What the page is drawn over: clear, in the paper's tone, so the filtered edges of the spread
    // and its index tabs fade into paper rather than black.
    constexpr D3DCOLOR kUnderPage = D3DCOLOR_ARGB(0, 0xA0, 0x80, 0x50);

    std::atomic<const void*> g_paperResource{nullptr};
    std::atomic<bool> g_newHud{false};

    // The rest on the render thread: the marker textures once seen; the target the page is drawn
    // into in place of the screen, and the one it stands in for while it is, with its viewport; the
    // page as the paper shows it, mip-mapped; and the paper's own texture it is bound in place of.
    IDirect3DBaseTexture9* g_begin = nullptr;
    IDirect3DBaseTexture9* g_end = nullptr;
    IDirect3DSurface9* g_screen = nullptr;
    IDirect3DSurface9* g_saved = nullptr;
    D3DVIEWPORT9 g_viewport{};
    IDirect3DTexture9* g_page = nullptr;
    IDirect3DBaseTexture9* g_paper = nullptr;

    Mark Measure(IDirect3DBaseTexture9* texture) {
        D3DSURFACE_DESC desc{};
        if (texture->GetType() != D3DRTYPE_TEXTURE ||
            FAILED(static_cast<IDirect3DTexture9*>(texture)->GetLevelDesc(0, &desc))) {
            return Mark::None;
        }
        if (desc.Width == kMarkShort && desc.Height == kMarkLong) {
            return Mark::Begin;
        }
        return desc.Width == kMarkLong && desc.Height == kMarkShort ? Mark::End : Mark::None;
    }

    // A marker once known is measured again whenever it is bound, in case another texture has taken
    // its address since; the others are measured only while `learning` and a marker is unknown.
    Mark MarkOf(IDirect3DBaseTexture9* texture, bool learning) {
        const bool known = texture == g_begin || texture == g_end;
        if (!known && (!learning || (g_begin != nullptr && g_end != nullptr))) {
            return Mark::None;
        }
        const Mark mark = Measure(texture);
        if (texture == g_begin || mark == Mark::Begin) {
            g_begin = mark == Mark::Begin ? texture : nullptr;
        }
        if (texture == g_end || mark == Mark::End) {
            g_end = mark == Mark::End ? texture : nullptr;
        }
        return mark;
    }

    // Null where the engine's pointers do not hold, as while the texture is still loading.
    IDirect3DBaseTexture9* Direct3DTextureOf(const void* resource) {
        __try {
            return Read<IDirect3DBaseTexture9*>(
                Read<const void*>(Read<const void*>(resource, kResourceTexture), kTextureDevice), kDeviceDirect3D);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return nullptr;
        }
    }

    void ReleaseAll() {
        Release(g_screen);
        Release(g_saved);
        Release(g_page);
        g_paper = nullptr;
    }

    // Makes the targets for a screen of this kind, again when it changed size.
    bool Ready(IDirect3DDevice9* device, const D3DSURFACE_DESC& screen) {
        D3DSURFACE_DESC made{};
        if (g_screen != nullptr && SUCCEEDED(g_screen->GetDesc(&made)) && made.Width == screen.Width &&
            made.Height == screen.Height) {
            return true;
        }
        ReleaseAll();
        // With alpha, which cuts the pages out where the sheet has no paper.
        if (FAILED(device->CreateRenderTarget(screen.Width, screen.Height, D3DFMT_A8R8G8B8, screen.MultiSampleType,
                                              screen.MultiSampleQuality, FALSE, &g_screen, nullptr)) ||
            FAILED(device->CreateTexture(screen.Width, screen.Height, 0, D3DUSAGE_RENDERTARGET | D3DUSAGE_AUTOGENMIPMAP,
                                         D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT, &g_page, nullptr))) {
            FCSE::Logf("paper: no target could be made for a %ux%u screen", screen.Width, screen.Height);
            ReleaseAll();
            return false;
        }
        FCSE::Logf("paper: the page is drawn off the %ux%u screen", screen.Width, screen.Height);
        return true;
    }

    void Begin(IDirect3DDevice9* device) {
        IDirect3DSurface9* target = nullptr;
        D3DSURFACE_DESC screen{};
        if (FAILED(device->GetRenderTarget(0, &target))) {
            return;
        }
        if (FAILED(target->GetDesc(&screen)) || !Ready(device, screen)) {
            target->Release();
            return;
        }
        g_saved = target;
        device->GetViewport(&g_viewport);
        device->SetRenderTarget(0, g_screen);
        device->SetViewport(&g_viewport);
        device->Clear(0, nullptr, D3DCLEAR_TARGET, kUnderPage, 0.0f, 0);
    }

    void Restore(IDirect3DDevice9* device) {
        device->SetRenderTarget(0, g_saved);
        device->SetViewport(&g_viewport);
        Release(g_saved);
    }

    // Asked once a page: streaming puts a new Direct3D texture behind the paper's resource once the
    // full picture has loaded.
    void End(IDirect3DDevice9* device) {
        Restore(device);
        IDirect3DSurface9* page = nullptr;
        if (SUCCEEDED(g_page->GetSurfaceLevel(0, &page))) {
            const bool copied = SUCCEEDED(device->StretchRect(g_screen, nullptr, page, nullptr, D3DTEXF_NONE));
            page->Release();
            g_page->GenerateMipSubLevels();
            const void* resource = g_paperResource;
            g_paper = copied && resource != nullptr ? Direct3DTextureOf(resource) : nullptr;
        }
    }

    void __fastcall OnSetMapTexture(void* strategy, void* unused, void* resource, uint32_t owned) {
        g_paperResource = resource;
        g_setMapTexture(strategy, unused, resource, owned);
    }

    // The markers redirect whenever they are bound: the HUD being drawn may be a frame behind the
    // notebook, and a page let through would cover the screen.
    HRESULT __stdcall OnSetTexture(IDirect3DDevice9* device, DWORD stage, IDirect3DBaseTexture9* texture) {
        if (texture == nullptr) {
            return g_setTexture(device, stage, texture);
        }
        if (g_newHud.load(std::memory_order_relaxed) && g_newHud.exchange(false)) {
            g_begin = g_end = nullptr;
        }
        const bool open = Notebook::IsOpen();
        const Mark mark = MarkOf(texture, open);
        if (mark == Mark::Begin && g_saved == nullptr) {
            Begin(device);
        } else if (mark == Mark::End && g_saved != nullptr) {
            End(device);
        } else if (open && texture == g_paper) {
            return g_setTexture(device, stage, g_page);
        }
        return g_setTexture(device, stage, texture);
    }

    // A frame whose page was not finished, as when the HUD went away in the middle of it, goes back
    // to the screen.
    HRESULT __stdcall OnEndScene(IDirect3DDevice9* device) {
        if (g_saved != nullptr) {
            Restore(device);
        }
        return g_endScene(device);
    }

    // Targets made in the device's own memory have to go before it can be reset.
    HRESULT __stdcall OnReset(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters) {
        ReleaseAll();
        g_begin = g_end = nullptr;
        return g_reset(device, parameters);
    }
}

void NewHud() { g_newHud = true; }

void Install() {
    const bool hooked = Hook(g_setMapTextureSite, &OnSetMapTexture, &g_setMapTexture) &&
                        Vtable::Hook(Vtable::kSetTexture, &OnSetTexture, &g_setTexture) &&
                        Vtable::Hook(Vtable::kEndScene, &OnEndScene, &g_endScene) &&
                        Vtable::Hook(Vtable::kReset, &OnReset, &g_reset);
    FCSE::Logf("paper: %s", hooked ? "the page is drawn on the notebook's pages"
                                   : "off, the map's texture or the device was not found; the page is drawn over the screen");
}

}
