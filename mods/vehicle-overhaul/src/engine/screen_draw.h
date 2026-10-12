// A screen-space draw over someone else's frame, and the state it has to put back.
#pragma once

#include <d3d9.h>

namespace VehicleOverhaul {

// Everything a draw at the end of a pass disturbs, saved on construction, set to a plain baseline
// and put back when it goes out of scope: no depth test or write, no stencil, no culling, no
// lighting, no fog, no scissor, no alpha test or blend, no depth bias, no alpha-to-coverage, full
// colour writes, point-sampled clamped textures, no shaders, and the whole viewport at its natural
// depth range. A caller that needs something else sets it after construction. The engine filters
// redundant state, so every change has to be undone exactly.
//
// `firstConstant` and `constantCount` are the pixel shader constant registers the caller will
// write, which are the ones saved.
class ScreenDraw {
public:
    ScreenDraw(IDirect3DDevice9* device, UINT firstConstant, UINT constantCount);
    ~ScreenDraw();

    ScreenDraw(const ScreenDraw&) = delete;
    ScreenDraw& operator=(const ScreenDraw&) = delete;

    // A quad over the rectangle, in pixels, with texture coordinates running zero to one across it
    // and the half-pixel offset that lands texels on pixel centres. `depth` is what the depth test
    // compares. Drawn through the fixed-function vertex pipeline, so it pairs only with a pixel
    // shader.
    void Quad(float left, float top, float right, float bottom, float depth = 0.0f);

private:
    static constexpr UINT kMaxConstantRegisters = 8;

    static constexpr D3DRENDERSTATETYPE kRenderStates[] = {
        D3DRS_ZENABLE,         D3DRS_ZWRITEENABLE,      D3DRS_ZFUNC,
        D3DRS_CULLMODE,        D3DRS_LIGHTING,          D3DRS_FOGENABLE,
        D3DRS_STENCILENABLE,   D3DRS_SCISSORTESTENABLE, D3DRS_COLORWRITEENABLE,
        D3DRS_ALPHATESTENABLE, D3DRS_ALPHABLENDENABLE,  D3DRS_SEPARATEALPHABLENDENABLE,
        D3DRS_SRCBLEND,        D3DRS_DESTBLEND,         D3DRS_SRGBWRITEENABLE,
        D3DRS_CLIPPLANEENABLE, D3DRS_SHADEMODE,         D3DRS_BLENDOP,
        D3DRS_DEPTHBIAS,       D3DRS_SLOPESCALEDEPTHBIAS,
        D3DRS_MULTISAMPLEANTIALIAS, D3DRS_MULTISAMPLEMASK,
        // The two the drivers overload to turn alpha into coverage.
        D3DRS_POINTSIZE,       D3DRS_ADAPTIVETESS_Y,
    };

    // The two that still steer the fixed-function vertex side's texture coordinates even with a
    // pixel shader bound, alongside the colour and alpha pipeline a shaderless draw uses.
    static constexpr D3DTEXTURESTAGESTATETYPE kStageStates[] = {
        D3DTSS_COLOROP,   D3DTSS_COLORARG1,     D3DTSS_ALPHAOP,
        D3DTSS_ALPHAARG1, D3DTSS_TEXCOORDINDEX, D3DTSS_TEXTURETRANSFORMFLAGS,
    };

    static constexpr size_t kRenderStateCount = sizeof(kRenderStates) / sizeof(kRenderStates[0]);
    static constexpr size_t kStageStateCount = sizeof(kStageStates) / sizeof(kStageStates[0]);

    IDirect3DDevice9* m_device;
    IDirect3DVertexShader9* m_vertexShader = nullptr;
    IDirect3DPixelShader9* m_pixelShader = nullptr;
    IDirect3DVertexDeclaration9* m_vertexDeclaration = nullptr;
    IDirect3DVertexBuffer9* m_stream = nullptr;
    UINT m_streamOffset = 0;
    UINT m_streamStride = 0;
    DWORD m_vertexFormat = 0;
    UINT m_firstConstant;
    UINT m_constantCount;
    float m_pixelConstants[kMaxConstantRegisters * 4];
    DWORD m_renderStates[kRenderStateCount];
    DWORD m_stageStates[kStageStateCount];
    D3DVIEWPORT9 m_viewport;
};

}
