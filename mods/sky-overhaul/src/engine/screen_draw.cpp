#include "engine/screen_draw.h"

namespace {
    constexpr D3DRENDERSTATETYPE kRenderStates[] = {
        D3DRS_ZENABLE,          D3DRS_ZWRITEENABLE,      D3DRS_ZFUNC,
        D3DRS_CULLMODE,         D3DRS_LIGHTING,          D3DRS_FOGENABLE,
        D3DRS_STENCILENABLE,    D3DRS_SCISSORTESTENABLE, D3DRS_COLORWRITEENABLE,
        D3DRS_ALPHATESTENABLE,  D3DRS_ALPHABLENDENABLE,  D3DRS_SEPARATEALPHABLENDENABLE,
        D3DRS_SRGBWRITEENABLE,  D3DRS_CLIPPLANEENABLE,   D3DRS_SHADEMODE,
        D3DRS_BLENDOP,
    };

    // The two that still steer the fixed-function vertex side's texture coordinates even with a
    // pixel shader bound, alongside the colour and alpha pipeline a shaderless draw uses.
    constexpr D3DTEXTURESTAGESTATETYPE kStageStates[] = {
        D3DTSS_COLOROP,        D3DTSS_COLORARG1,           D3DTSS_ALPHAOP,
        D3DTSS_ALPHAARG1,      D3DTSS_TEXCOORDINDEX,       D3DTSS_TEXTURETRANSFORMFLAGS,
    };

    constexpr D3DSAMPLERSTATETYPE kSamplerStates[] = {
        D3DSAMP_ADDRESSU, D3DSAMP_ADDRESSV, D3DSAMP_MAGFILTER,
        D3DSAMP_MINFILTER, D3DSAMP_MIPFILTER, D3DSAMP_SRGBTEXTURE,
    };

    constexpr DWORD kVertexFormat = D3DFVF_XYZRHW | D3DFVF_TEX1;

    struct ScreenVertex {
        float x, y, z, rhw;
        float u, v;
    };
}

SkyOverhaul::ScreenDraw::ScreenDraw(IDirect3DDevice9* device) : m_device(device) {
    for (size_t i = 0; i < sizeof(kRenderStates) / sizeof(kRenderStates[0]); i++) {
        m_device->GetRenderState(kRenderStates[i], &m_renderStates[i]);
    }
    for (size_t i = 0; i < sizeof(kStageStates) / sizeof(kStageStates[0]); i++) {
        m_device->GetTextureStageState(0, kStageStates[i], &m_stageStates[i]);
    }
    for (DWORD sampler = 0; sampler < 2; sampler++) {
        for (size_t i = 0; i < sizeof(kSamplerStates) / sizeof(kSamplerStates[0]); i++) {
            m_device->GetSamplerState(sampler, kSamplerStates[i], &m_samplerStates[sampler][i]);
        }
    }

    m_device->GetVertexShader(&m_vertexShader);
    m_device->GetPixelShader(&m_pixelShader);
    m_device->GetVertexDeclaration(&m_vertexDeclaration);
    m_device->GetTexture(0, &m_textures[0]);
    m_device->GetTexture(1, &m_textures[1]);
    m_device->GetStreamSource(0, &m_stream, &m_streamOffset, &m_streamStride);
    m_device->GetIndices(&m_indices);
    m_device->GetFVF(&m_vertexFormat);
    m_device->GetViewport(&m_viewport);
    m_device->GetPixelShaderConstantF(0, m_pixelConstants, 2);

    m_device->SetVertexShader(nullptr);
    m_device->SetPixelShader(nullptr);
    m_device->SetFVF(kVertexFormat);

    m_device->SetRenderState(D3DRS_ZENABLE, D3DZB_FALSE);
    m_device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
    m_device->SetRenderState(D3DRS_ZFUNC, D3DCMP_LESSEQUAL);
    m_device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
    m_device->SetRenderState(D3DRS_LIGHTING, FALSE);
    m_device->SetRenderState(D3DRS_FOGENABLE, FALSE);
    m_device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
    m_device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
    m_device->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
    m_device->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE);
    m_device->SetRenderState(D3DRS_SEPARATEALPHABLENDENABLE, FALSE);
    m_device->SetRenderState(D3DRS_SRGBWRITEENABLE, FALSE);
    m_device->SetRenderState(D3DRS_CLIPPLANEENABLE, 0);
    m_device->SetRenderState(D3DRS_SHADEMODE, D3DSHADE_GOURAUD);
    m_device->SetRenderState(D3DRS_BLENDOP, D3DBLENDOP_ADD);
    m_device->SetRenderState(D3DRS_COLORWRITEENABLE,
                             D3DCOLORWRITEENABLE_RED | D3DCOLORWRITEENABLE_GREEN |
                                 D3DCOLORWRITEENABLE_BLUE | D3DCOLORWRITEENABLE_ALPHA);

    m_device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
    m_device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_TEXTURE);
    m_device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
    m_device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_TEXTURE);
    m_device->SetTextureStageState(0, D3DTSS_TEXCOORDINDEX, 0);
    m_device->SetTextureStageState(0, D3DTSS_TEXTURETRANSFORMFLAGS, D3DTTFF_DISABLE);

    for (DWORD sampler = 0; sampler < 2; sampler++) {
        m_device->SetSamplerState(sampler, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
        m_device->SetSamplerState(sampler, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
        m_device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
        m_device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_POINT);
        m_device->SetSamplerState(sampler, D3DSAMP_MIPFILTER, D3DTEXF_NONE);
        m_device->SetSamplerState(sampler, D3DSAMP_SRGBTEXTURE, FALSE);
    }

    // The sky pass draws through a viewport squeezed against the far plane, which would otherwise
    // remap the quad's own depth along with everything else.
    D3DVIEWPORT9 wholeRange = m_viewport;
    wholeRange.MinZ = 0.0f;
    wholeRange.MaxZ = 1.0f;
    m_device->SetViewport(&wholeRange);
}

SkyOverhaul::ScreenDraw::~ScreenDraw() {
    for (size_t i = 0; i < sizeof(kRenderStates) / sizeof(kRenderStates[0]); i++) {
        m_device->SetRenderState(kRenderStates[i], m_renderStates[i]);
    }
    for (size_t i = 0; i < sizeof(kStageStates) / sizeof(kStageStates[0]); i++) {
        m_device->SetTextureStageState(0, kStageStates[i], m_stageStates[i]);
    }
    for (DWORD sampler = 0; sampler < 2; sampler++) {
        for (size_t i = 0; i < sizeof(kSamplerStates) / sizeof(kSamplerStates[0]); i++) {
            m_device->SetSamplerState(sampler, kSamplerStates[i], m_samplerStates[sampler][i]);
        }
    }

    m_device->SetVertexShader(m_vertexShader);
    m_device->SetPixelShader(m_pixelShader);
    m_device->SetVertexDeclaration(m_vertexDeclaration);
    m_device->SetTexture(0, m_textures[0]);
    m_device->SetTexture(1, m_textures[1]);
    // DrawPrimitiveUP leaves stream zero unbound, and an engine that filters redundant binds would
    // then draw nothing for the rest of the frame.
    m_device->SetStreamSource(0, m_stream, m_streamOffset, m_streamStride);
    m_device->SetIndices(m_indices);
    m_device->SetFVF(m_vertexFormat);
    m_device->SetViewport(&m_viewport);
    m_device->SetPixelShaderConstantF(0, m_pixelConstants, 2);

    if (m_vertexShader != nullptr) {
        m_vertexShader->Release();
    }
    if (m_pixelShader != nullptr) {
        m_pixelShader->Release();
    }
    if (m_vertexDeclaration != nullptr) {
        m_vertexDeclaration->Release();
    }
    for (IDirect3DBaseTexture9* texture : m_textures) {
        if (texture != nullptr) {
            texture->Release();
        }
    }
    if (m_stream != nullptr) {
        m_stream->Release();
    }
    if (m_indices != nullptr) {
        m_indices->Release();
    }
}

void SkyOverhaul::ScreenDraw::Quad(float left, float top, float right, float bottom, float depth) {
    const ScreenVertex quad[4] = {
        {left - 0.5f, top - 0.5f, depth, 1.0f, 0.0f, 0.0f},
        {right - 0.5f, top - 0.5f, depth, 1.0f, 1.0f, 0.0f},
        {left - 0.5f, bottom - 0.5f, depth, 1.0f, 0.0f, 1.0f},
        {right - 0.5f, bottom - 0.5f, depth, 1.0f, 1.0f, 1.0f},
    };
    m_device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(ScreenVertex));
}
