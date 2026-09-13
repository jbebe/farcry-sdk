// A screen-space draw over someone else's frame, and the state it has to put back.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul {

// The least a draw of our own can disturb: the shaders, the geometry bindings and one range of
// pixel shader constants, saved on construction and put back when it goes out of scope. Nothing is
// set, so whatever the interrupted draw had bound is what this one draws under.
//
// That is what a draw put in place of one of the engine's needs, because the draws after it
// inherit the state the engine left: the sun and the stars are drawn with no blend or depth mode
// of their own, and take the sky dome's.
//
// `firstConstant` and `constantCount` are the pixel shader constant registers the caller will
// write, which are the ones saved. The engine's own globals live at c0 to c64, so a shader of our
// own puts its parameters above them.
class DrawGuard {
public:
    DrawGuard(IDirect3DDevice9* device, UINT firstConstant, UINT constantCount);
    ~DrawGuard();

    DrawGuard(const DrawGuard&) = delete;
    DrawGuard& operator=(const DrawGuard&) = delete;

    // A quad over the rectangle, in pixels, with texture coordinates running zero to one across
    // it and the half-pixel offset that lands texels on pixel centres. `depth` is what the depth
    // test compares, which only matters when the depth test is on. Drawn through the fixed-function
    // vertex pipeline, so it pairs only with a pixel shader.
    void Quad(float left, float top, float right, float bottom, float depth = 0.0f);

    // A quad over the whole viewport at `depth` in clip space, carrying one direction per corner
    // to a ps_3_0 pixel shader's TEXCOORD0 through a vertex shader of its own. False if the device
    // would not make that shader or its declaration.
    bool ClipQuad(float depth, const float corners[4][3]);

    // The same over a rectangle of the viewport, in pixels, with its edges where Quad puts them and
    // each corner's direction the one the whole quad gives that point of the screen.
    bool ClipQuad(float depth, const float corners[4][3], float left, float top, float right,
                  float bottom);

    // Drops the vertex shader and declaration ClipQuad keeps, which its next call makes again.
    static void ReleaseDeviceObjects();

protected:
    IDirect3DDevice9* m_device;

private:
    static constexpr UINT kMaxConstantRegisters = 16;

    IDirect3DVertexShader9* m_vertexShader = nullptr;
    IDirect3DPixelShader9* m_pixelShader = nullptr;
    IDirect3DVertexDeclaration9* m_vertexDeclaration = nullptr;
    IDirect3DVertexBuffer9* m_stream = nullptr;
    UINT m_streamOffset = 0;
    UINT m_streamStride = 0;
    IDirect3DIndexBuffer9* m_indices = nullptr;
    DWORD m_vertexFormat;
    UINT m_firstConstant;
    UINT m_constantCount;
    float m_pixelConstants[kMaxConstantRegisters * 4];
};

// The guard above, plus everything else a draw at the end of a pass disturbs, set to a plain
// baseline: no depth test or write, no stencil, no culling, no lighting, no fog, no scissor, no
// alpha test or blend, no depth bias, no alpha-to-coverage, full colour writes, point-sampled
// clamped textures, no shaders, and the whole viewport at its natural depth range. A caller that
// needs something else sets it after construction.
class ScreenDraw : public DrawGuard {
public:
    ScreenDraw(IDirect3DDevice9* device, UINT firstConstant, UINT constantCount);
    ~ScreenDraw();

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

    static constexpr size_t kRenderStateCount = sizeof(kRenderStates) / sizeof(kRenderStates[0]);
    static constexpr size_t kStageStateCount = sizeof(kStageStates) / sizeof(kStageStates[0]);
    static constexpr size_t kSamplerStateCount = sizeof(kSamplerStates) / sizeof(kSamplerStates[0]);

    DWORD m_renderStates[kRenderStateCount];
    DWORD m_stageStates[kStageStateCount];
    DWORD m_samplerStates[kSamplers][kSamplerStateCount];
    IDirect3DBaseTexture9* m_textures[kSamplers] = {};
    D3DVIEWPORT9 m_viewport;
};

}
