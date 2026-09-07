// A screen-space draw over someone else's frame, and the state it has to put back.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul {

// Saves everything a screen-space draw disturbs, sets a plain baseline, and restores it all when
// it goes out of scope. The baseline is: no depth test or write, no stencil, no culling, no
// lighting, no fog, no scissor, no alpha test or blend, no depth bias, no alpha-to-coverage, full
// colour writes, point-sampled clamped textures, no shaders, and the whole viewport at its natural
// depth range. A caller that needs something else sets it after construction.
//
// `firstConstant` and `constantCount` are the pixel shader constant registers the caller will
// write, which are the ones saved. The engine's own globals live at c0 to c64, so a shader of our
// own puts its parameters above them.
class ScreenDraw {
public:
    ScreenDraw(IDirect3DDevice9* device, UINT firstConstant, UINT constantCount);
    ~ScreenDraw();

    ScreenDraw(const ScreenDraw&) = delete;
    ScreenDraw& operator=(const ScreenDraw&) = delete;

    // A quad over the rectangle, in pixels, with texture coordinates running zero to one across
    // it and the half-pixel offset that lands texels on pixel centres. `depth` is what the depth
    // test compares, which only matters when the caller has turned that test back on. Drawn
    // through the fixed-function vertex pipeline, so it pairs only with a pixel shader.
    void Quad(float left, float top, float right, float bottom, float depth = 0.0f);

    // A quad over the whole viewport at `depth` in clip space, carrying one direction per corner
    // for the pixel shader to interpolate. Needs a vertex shader bound, which is what a pixel
    // shader past ps_2_b requires anyway. False if the vertex declaration could not be made.
    bool ClipQuad(float depth, const float corners[4][3]);

    // Drops the vertex declaration ClipQuad keeps. A declaration survives a device reset, so this
    // is only for a device going away.
    static void ReleaseDeviceObjects();

private:
    static constexpr D3DRENDERSTATETYPE kRenderStates[] = {
        D3DRS_ZENABLE,         D3DRS_ZWRITEENABLE,      D3DRS_ZFUNC,
        D3DRS_CULLMODE,        D3DRS_LIGHTING,          D3DRS_FOGENABLE,
        D3DRS_STENCILENABLE,   D3DRS_SCISSORTESTENABLE, D3DRS_COLORWRITEENABLE,
        D3DRS_ALPHATESTENABLE, D3DRS_ALPHABLENDENABLE,  D3DRS_SEPARATEALPHABLENDENABLE,
        D3DRS_SRCBLEND,        D3DRS_DESTBLEND,         D3DRS_SRGBWRITEENABLE,
        D3DRS_CLIPPLANEENABLE, D3DRS_SHADEMODE,         D3DRS_BLENDOP,
        D3DRS_DEPTHBIAS,       D3DRS_SLOPESCALEDEPTHBIAS,
        D3DRS_MULTISAMPLEANTIALIAS, D3DRS_MULTISAMPLEMASK,
        // The two the drivers overload to turn alpha into coverage, which would dither a quad
        // whose alpha is a transmittance rather than an opacity.
        D3DRS_POINTSIZE,       D3DRS_ADAPTIVETESS_Y,
    };

    // The two that still steer the fixed-function vertex side's texture coordinates even with a
    // pixel shader bound, alongside the colour and alpha pipeline a shaderless draw uses.
    static constexpr D3DTEXTURESTAGESTATETYPE kStageStates[] = {
        D3DTSS_COLOROP,   D3DTSS_COLORARG1,     D3DTSS_ALPHAOP,
        D3DTSS_ALPHAARG1, D3DTSS_TEXCOORDINDEX, D3DTSS_TEXTURETRANSFORMFLAGS,
    };

    static constexpr D3DSAMPLERSTATETYPE kSamplerStates[] = {
        D3DSAMP_ADDRESSU,   D3DSAMP_ADDRESSV,      D3DSAMP_ADDRESSW,
        D3DSAMP_MAGFILTER,  D3DSAMP_MINFILTER,     D3DSAMP_MIPFILTER,
        D3DSAMP_SRGBTEXTURE, D3DSAMP_MIPMAPLODBIAS, D3DSAMP_MAXMIPLEVEL,
    };

    // As many as the effects bind, which is what has to be put back.
    static constexpr DWORD kSamplers = 3;
    static constexpr UINT kMaxConstantRegisters = 16;

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
    UINT m_firstConstant;
    UINT m_constantCount;
    float m_pixelConstants[kMaxConstantRegisters * 4];
};

}
