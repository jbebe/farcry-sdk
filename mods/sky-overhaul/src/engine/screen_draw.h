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
    static constexpr D3DRENDERSTATETYPE kRenderStates[] = {
        D3DRS_ZENABLE,         D3DRS_ZWRITEENABLE,      D3DRS_ZFUNC,
        D3DRS_CULLMODE,        D3DRS_LIGHTING,          D3DRS_FOGENABLE,
        D3DRS_STENCILENABLE,   D3DRS_SCISSORTESTENABLE, D3DRS_COLORWRITEENABLE,
        D3DRS_ALPHATESTENABLE, D3DRS_ALPHABLENDENABLE,  D3DRS_SEPARATEALPHABLENDENABLE,
        D3DRS_SRCBLEND,        D3DRS_DESTBLEND,         D3DRS_SRGBWRITEENABLE,
        D3DRS_CLIPPLANEENABLE, D3DRS_SHADEMODE,         D3DRS_BLENDOP,
    };

    // The two that still steer the fixed-function vertex side's texture coordinates even with a
    // pixel shader bound, alongside the colour and alpha pipeline a shaderless draw uses.
    static constexpr D3DTEXTURESTAGESTATETYPE kStageStates[] = {
        D3DTSS_COLOROP,   D3DTSS_COLORARG1,     D3DTSS_ALPHAOP,
        D3DTSS_ALPHAARG1, D3DTSS_TEXCOORDINDEX, D3DTSS_TEXTURETRANSFORMFLAGS,
    };

    static constexpr D3DSAMPLERSTATETYPE kSamplerStates[] = {
        D3DSAMP_ADDRESSU,  D3DSAMP_ADDRESSV,  D3DSAMP_MAGFILTER,
        D3DSAMP_MINFILTER, D3DSAMP_MIPFILTER, D3DSAMP_SRGBTEXTURE,
    };

    // As many as the effect binds and uploads, which is what has to be put back.
    static constexpr DWORD kSamplers = 3;
    static constexpr UINT kPixelConstantRegisters = 6;

    static constexpr size_t kRenderStateCount = sizeof(kRenderStates) / sizeof(kRenderStates[0]);
    static constexpr size_t kStageStateCount = sizeof(kStageStates) / sizeof(kStageStates[0]);
    static constexpr size_t kSamplerStateCount = sizeof(kSamplerStates) / sizeof(kSamplerStates[0]);

    IDirect3DDevice9* m_device;
    DWORD m_renderStates[kRenderStateCount];
    DWORD m_stageStates[kStageStateCount];
    DWORD m_samplerStates[kSamplers][kSamplerStateCount];
    IDirect3DVertexShader9* m_vertexShader = nullptr;
    IDirect3DPixelShader9* m_pixelShader = nullptr;
    IDirect3DVertexDeclaration9* m_vertexDeclaration = nullptr;
    IDirect3DBaseTexture9* m_textures[kSamplers] = {};
    IDirect3DVertexBuffer9* m_stream = nullptr;
    UINT m_streamOffset = 0;
    UINT m_streamStride = 0;
    IDirect3DIndexBuffer9* m_indices = nullptr;
    DWORD m_vertexFormat;
    D3DVIEWPORT9 m_viewport;
    float m_pixelConstants[kPixelConstantRegisters * 4];
};

}
