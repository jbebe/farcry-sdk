#include "engine/world_draw.h"

#include "engine/vtable.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>
#include <d3d9.h>

namespace {
    using DevTools::WorldDraw::Frame;
    using DevTools::WorldDraw::Vertex;

    constexpr size_t kEndSceneSlot = 42;

    // The sky pass is the only world pass whose depth range is squeezed against the far plane.
    constexpr float kSkyPassMinZ = 0.9f;

    // c4 to c11: world to clip, then the projection, which the engine binds for every world shader
    // one row per register; c45: where the camera is.
    constexpr UINT kTransformRegister = 4;
    constexpr UINT kTransformRows = 8;
    constexpr UINT kCameraRegister = 45;
    // The projection's vertical scale, one over the tangent of half the vertical field of view.
    constexpr size_t kVerticalScale = 16 + 5;

    // How wide a line is on screen.
    constexpr float kLinePixels = 3.0f;

    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9* device);

    EndSceneFn g_originalEndScene = nullptr;
    std::atomic<DevTools::WorldDraw::FillFn> g_fill{nullptr};
    Frame g_frame;
    std::vector<Vertex> g_ribbons;

    struct Surface {
        IDirect3DSurface9* surface = nullptr;

        ~Surface() {
            if (surface != nullptr) {
                surface->Release();
            }
        }
    };

    // Whether the pass renders the world: an offscreen target of the back buffer's size, with depth.
    bool IsWorld(IDirect3DDevice9* device) {
        Surface target;
        Surface backBuffer;
        Surface depth;
        if (FAILED(device->GetRenderTarget(0, &target.surface)) || target.surface == nullptr ||
            FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer.surface)) ||
            backBuffer.surface == nullptr || target.surface == backBuffer.surface ||
            FAILED(device->GetDepthStencilSurface(&depth.surface)) || depth.surface == nullptr) {
            return false;
        }
        D3DSURFACE_DESC targetDesc = {};
        D3DSURFACE_DESC backBufferDesc = {};
        target.surface->GetDesc(&targetDesc);
        backBuffer.surface->GetDesc(&backBufferDesc);
        return targetDesc.Width == backBufferDesc.Width && targetDesc.Height == backBufferDesc.Height;
    }

    // The two edges of a line at `at`, running along `along`, turned to face the camera and as far
    // apart as `metresPerPixel` makes kLinePixels at `at`'s distance.
    void Edges(const Vertex& at, const float along[3], const float camera[3], float metresPerPixel,
               Vertex& minus, Vertex& plus) {
        const float toCamera[3] = {camera[0] - at.x, camera[1] - at.y, camera[2] - at.z};
        const float side[3] = {along[1] * toCamera[2] - along[2] * toCamera[1],
                               along[2] * toCamera[0] - along[0] * toCamera[2],
                               along[0] * toCamera[1] - along[1] * toCamera[0]};
        const float length = std::sqrt(side[0] * side[0] + side[1] * side[1] + side[2] * side[2]);
        const float distance = std::sqrt(toCamera[0] * toCamera[0] + toCamera[1] * toCamera[1] +
                                         toCamera[2] * toCamera[2]);
        const float half = length > 0.0f ? kLinePixels * 0.5f * metresPerPixel * distance / length
                                         : 0.0f;
        minus = {at.x - side[0] * half, at.y - side[1] * half, at.z - side[2] * half, at.colour};
        plus = {at.x + side[0] * half, at.y + side[1] * half, at.z + side[2] * half, at.colour};
    }

    // Each line as two triangles facing the camera, kLinePixels wide on screen.
    void Ribbons(const std::vector<Vertex>& lines, const float camera[3], float metresPerPixel) {
        g_ribbons.clear();
        for (size_t i = 0; i + 1 < lines.size(); i += 2) {
            const Vertex& a = lines[i];
            const Vertex& b = lines[i + 1];
            const float along[3] = {b.x - a.x, b.y - a.y, b.z - a.z};
            Vertex a0, a1, b0, b1;
            Edges(a, along, camera, metresPerPixel, a0, a1);
            Edges(b, along, camera, metresPerPixel, b0, b1);
            g_ribbons.insert(g_ribbons.end(), {a0, a1, b1, a0, b1, b0});
        }
    }

    void Triangles(IDirect3DDevice9* device, const std::vector<Vertex>& vertices) {
        if (vertices.size() >= 3) {
            device->DrawPrimitiveUP(D3DPT_TRIANGLELIST, static_cast<UINT>(vertices.size() / 3),
                                    vertices.data(), sizeof(Vertex));
        }
    }

    void Draw(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport,
              DevTools::WorldDraw::FillFn fill) {
        g_frame.lines.clear();
        g_frame.triangles.clear();
        fill(g_frame);
        if (g_frame.lines.empty() && g_frame.triangles.empty()) {
            return;
        }
        float rows[kTransformRows * 4];
        float camera[4];
        IDirect3DStateBlock9* saved = nullptr;
        if (FAILED(device->GetVertexShaderConstantF(kTransformRegister, rows, kTransformRows)) ||
            FAILED(device->GetVertexShaderConstantF(kCameraRegister, camera, 1)) ||
            rows[kVerticalScale] <= 0.0f || viewport.Height == 0 ||
            FAILED(device->CreateStateBlock(D3DSBT_ALL, &saved))) {
            return;
        }
        // The height of the view at one metre's distance, over its height in pixels.
        Ribbons(g_frame.lines, camera, 2.0f / (rows[kVerticalScale] * viewport.Height));

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

        // The sky pass's depth range would push every shape against the far plane.
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
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
        device->SetRenderState(D3DRS_BLENDOP, D3DBLENDOP_ADD);
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

        Triangles(device, g_frame.triangles);
        Triangles(device, g_ribbons);

        saved->Apply();
        saved->Release();
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        const DevTools::WorldDraw::FillFn fill = g_fill.load(std::memory_order_relaxed);
        D3DVIEWPORT9 viewport = {};
        if (fill != nullptr && SUCCEEDED(device->GetViewport(&viewport)) &&
            viewport.MinZ >= kSkyPassMinZ && IsWorld(device) &&
            device->TestCooperativeLevel() == D3D_OK) {
            Draw(device, viewport, fill);
        }
        return g_originalEndScene(device);
    }
}

namespace DevTools::WorldDraw {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    void* endScene = Vtable::Slot(kEndSceneSlot);
    if (endScene == nullptr) {
        api->Log("world draw: no Direct3D 9 device could be created - nothing can be drawn");
        return;
    }
    // A rejected hook is already logged by FCSE.
    api->Hook(endScene, reinterpret_cast<void*>(&EndSceneDetour),
              reinterpret_cast<void**>(&g_originalEndScene));
}

void SetFill(FillFn fill) {
    g_fill.store(fill, std::memory_order_relaxed);
}

}
