// A screen-space draw over someone else's frame, and the state it has to put back.
#pragma once

#include <d3d9.h>

#include <cstddef>
#include <iterator>

namespace WeaponOverhaul {

// Everything a draw at the end of a pass disturbs, saved on construction, set to a plain baseline
// and put back when it goes out of scope: the targets, the shaders and geometry bindings, one range
// of pixel shader constants, and render, stage and sampler states. The baseline is no depth test or
// write, no stencil, culling, fog, scissor, alpha test or blend, full colour writes, point-sampled
// clamped textures, and the whole viewport at its natural depth range.
//
// `firstConstant` and `constantCount` are the pixel shader registers the caller will write.
class ScreenDraw {
public:
    ScreenDraw(IDirect3DDevice9* device, UINT firstConstant, UINT constantCount);
    ~ScreenDraw();

    ScreenDraw(const ScreenDraw&) = delete;
    ScreenDraw& operator=(const ScreenDraw&) = delete;

    // A quad over the rectangle, in pixels, with texture coordinates running zero to one across it
    // and the half-pixel offset that lands texels on pixel centres. Drawn through the fixed-function
    // vertex pipeline, so it pairs only with a ps_2_x pixel shader.
    void Quad(float left, float top, float right, float bottom);

private:
    static constexpr UINT kMaxConstantRegisters = 8;
    static constexpr DWORD kSamplers = 6;

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

    static constexpr D3DTEXTURESTAGESTATETYPE kStageStates[] = {
        D3DTSS_TEXCOORDINDEX,
        D3DTSS_TEXTURETRANSFORMFLAGS,
    };

    static constexpr D3DSAMPLERSTATETYPE kSamplerStates[] = {
        D3DSAMP_ADDRESSU,   D3DSAMP_ADDRESSV,      D3DSAMP_ADDRESSW,
        D3DSAMP_MAGFILTER,  D3DSAMP_MINFILTER,     D3DSAMP_MIPFILTER,
        D3DSAMP_SRGBTEXTURE, D3DSAMP_MIPMAPLODBIAS, D3DSAMP_MAXMIPLEVEL,
    };

    static constexpr size_t kRenderStateCount = std::size(kRenderStates);
    static constexpr size_t kStageStateCount = std::size(kStageStates);
    static constexpr size_t kSamplerStateCount = std::size(kSamplerStates);

    IDirect3DDevice9* m_device;
    IDirect3DSurface9* m_target = nullptr;
    IDirect3DSurface9* m_depth = nullptr;
    RECT m_scissor = {};
    D3DVIEWPORT9 m_viewport = {};
    IDirect3DVertexShader9* m_vertexShader = nullptr;
    IDirect3DPixelShader9* m_pixelShader = nullptr;
    IDirect3DVertexDeclaration9* m_vertexDeclaration = nullptr;
    IDirect3DVertexBuffer9* m_stream = nullptr;
    UINT m_streamOffset = 0;
    UINT m_streamStride = 0;
    IDirect3DIndexBuffer9* m_indices = nullptr;
    DWORD m_vertexFormat = 0;
    UINT m_firstConstant;
    UINT m_constantCount;
    float m_pixelConstants[kMaxConstantRegisters * 4] = {};
    DWORD m_renderStates[kRenderStateCount] = {};
    DWORD m_stageStates[kStageStateCount] = {};
    DWORD m_samplerStates[kSamplers][kSamplerStateCount] = {};
    IDirect3DBaseTexture9* m_textures[kSamplers] = {};
};

}
