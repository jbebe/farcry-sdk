// A screen-space draw over someone else's frame, and the state it has to put back.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul {

// Saves everything a screen-space draw disturbs, sets a plain baseline, and restores it all when
// it goes out of scope. The baseline is: no depth test or write, no stencil, no culling, no
// lighting, no fog, no scissor, no alpha test or blend, full colour writes, point-sampled clamped
// textures, no shaders, pre-transformed vertices, and the whole viewport at its natural depth
// range. A caller that needs something else sets it after construction.
class ScreenDraw {
public:
    explicit ScreenDraw(IDirect3DDevice9* device);
    ~ScreenDraw();

    ScreenDraw(const ScreenDraw&) = delete;
    ScreenDraw& operator=(const ScreenDraw&) = delete;

    // A quad over the rectangle, in pixels, with texture coordinates running zero to one across
    // it and the half-pixel offset that lands texels on pixel centres. `depth` is what the depth
    // test compares, which only matters when the caller has turned that test back on.
    void Quad(float left, float top, float right, float bottom, float depth = 0.0f);

private:
    IDirect3DDevice9* m_device;
    DWORD m_renderStates[20] = {};
    DWORD m_stageStates[6] = {};
    DWORD m_samplerStates[2][6] = {};
    IDirect3DVertexShader9* m_vertexShader = nullptr;
    IDirect3DPixelShader9* m_pixelShader = nullptr;
    IDirect3DVertexDeclaration9* m_vertexDeclaration = nullptr;
    IDirect3DBaseTexture9* m_textures[2] = {};
    IDirect3DVertexBuffer9* m_stream = nullptr;
    UINT m_streamOffset = 0;
    UINT m_streamStride = 0;
    IDirect3DIndexBuffer9* m_indices = nullptr;
    DWORD m_vertexFormat = 0;
    D3DVIEWPORT9 m_viewport = {};
    float m_pixelConstants[12] = {};
};

}
