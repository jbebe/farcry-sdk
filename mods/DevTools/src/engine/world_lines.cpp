#include "engine/world_lines.h"

#include "engine/renderer.h"
#include "fcse_api.h"

#include <atomic>
#include <d3d9.h>

namespace {
    using DevTools::WorldLines::Vertex;

    constexpr size_t kEndSceneSlot = 42;

    // The sky pass is the only world pass whose depth range is squeezed against the far plane.
    constexpr float kSkyPassMinZ = 0.9f;

    // c4 to c7: world to clip, which the engine binds for every world shader, one row per register.
    constexpr UINT kViewProjectionRegister = 4;
    constexpr UINT kViewProjectionRows = 4;

    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9* device);

    EndSceneFn g_originalEndScene = nullptr;
    DevTools::WorldLines::FillFn g_fill = nullptr;
    std::atomic<bool> g_enabled{false};
    std::vector<Vertex> g_vertices;
    // Whether this frame's sky pass has had its lines. The composite closes the frame.
    bool g_drawn = false;

    struct Surface {
        IDirect3DSurface9* surface = nullptr;

        ~Surface() {
            if (surface != nullptr) {
                surface->Release();
            }
        }
    };

    enum class Pass { Other, Scene, Composite };

    // The world renders into an offscreen target of the back buffer's size with depth attached; the
    // composite is the only back-buffer pass with no depth.
    Pass Classify(IDirect3DDevice9* device) {
        Surface target;
        Surface backBuffer;
        if (FAILED(device->GetRenderTarget(0, &target.surface)) || target.surface == nullptr ||
            FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) ||
            backBuffer.surface == nullptr) {
            return Pass::Other;
        }
        Surface depth;
        const bool haveDepth =
            SUCCEEDED(device->GetDepthStencilSurface(&depth.surface)) && depth.surface != nullptr;
        if (target.surface == backBuffer.surface) {
            return haveDepth ? Pass::Other : Pass::Composite;
        }
        D3DSURFACE_DESC targetDesc = {};
        D3DSURFACE_DESC backBufferDesc = {};
        target.surface->GetDesc(&targetDesc);
        backBuffer.surface->GetDesc(&backBufferDesc);
        const bool sceneSized =
            targetDesc.Width == backBufferDesc.Width && targetDesc.Height == backBufferDesc.Height;
        return haveDepth && sceneSized ? Pass::Scene : Pass::Other;
    }

    void Draw(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport) {
        g_vertices.clear();
        g_fill(g_vertices);
        if (g_vertices.size() < 2) {
            return;
        }
        float rows[16];
        IDirect3DStateBlock9* saved = nullptr;
        if (FAILED(device->GetVertexShaderConstantF(kViewProjectionRegister, rows,
                                                    kViewProjectionRows)) ||
            FAILED(device->CreateStateBlock(D3DSBT_ALL, &saved))) {
            return;
        }

        // Fixed function multiplies row vectors, so the engine's rows go in as columns.
        D3DMATRIX projection = {};
        for (int row = 0; row < 4; ++row) {
            for (int column = 0; column < 4; ++column) {
                projection.m[column][row] = rows[row * 4 + column];
            }
        }
        D3DMATRIX identity = {};
        identity._11 = identity._22 = identity._33 = identity._44 = 1.0f;
        device->SetTransform(D3DTS_WORLD, &identity);
        device->SetTransform(D3DTS_VIEW, &identity);
        device->SetTransform(D3DTS_PROJECTION, &projection);

        // The sky pass's depth range would push every line against the far plane.
        D3DVIEWPORT9 full = viewport;
        full.MinZ = 0.0f;
        full.MaxZ = 1.0f;
        device->SetViewport(&full);

        device->SetVertexShader(nullptr);
        device->SetPixelShader(nullptr);
        device->SetTexture(0, nullptr);
        device->SetFVF(D3DFVF_XYZ | D3DFVF_DIFFUSE);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
        device->SetRenderState(D3DRS_ZFUNC, D3DCMP_LESSEQUAL);
        device->SetRenderState(D3DRS_LIGHTING, FALSE);
        device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE);
        device->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
        device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
        device->SetRenderState(D3DRS_FOGENABLE, FALSE);
        device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
        device->SetRenderState(D3DRS_CLIPPING, TRUE);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, 0xF);
        device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_DIFFUSE);
        device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_DIFFUSE);
        device->SetTextureStageState(1, D3DTSS_COLOROP, D3DTOP_DISABLE);

        device->DrawPrimitiveUP(D3DPT_LINELIST, static_cast<UINT>(g_vertices.size() / 2),
                                g_vertices.data(), sizeof(Vertex));

        saved->Apply();
        saved->Release();
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        if (g_enabled.load(std::memory_order_relaxed) && device->TestCooperativeLevel() == D3D_OK) {
            switch (Classify(device)) {
            case Pass::Scene: {
                D3DVIEWPORT9 viewport = {};
                if (!g_drawn && SUCCEEDED(device->GetViewport(&viewport)) &&
                    viewport.MinZ >= kSkyPassMinZ) {
                    g_drawn = true;
                    Draw(device, viewport);
                }
                break;
            }
            case Pass::Composite:
                g_drawn = false;
                break;
            case Pass::Other:
                break;
            }
        }
        return g_originalEndScene(device);
    }
}

namespace DevTools::WorldLines {

bool Install(FillFn fill) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    void* endScene = Renderer::DeviceFunction(kEndSceneSlot);
    if (endScene == nullptr) {
        api->Log("world lines: no Direct3D 9 device could be created - nothing can be drawn");
        return false;
    }
    g_fill = fill;
    // A rejected hook is already logged by FCSE.
    return api->Hook(endScene, reinterpret_cast<void*>(&EndSceneDetour),
                     reinterpret_cast<void**>(&g_originalEndScene));
}

void SetEnabled(bool enabled) {
    g_enabled.store(enabled, std::memory_order_relaxed);
}

}
