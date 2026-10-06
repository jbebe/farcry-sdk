#include "engine/screen_draw.h"

#include "engine/com.h"

#include <vector>

namespace {
    constexpr DWORD kVertexFormat = D3DFVF_XYZRHW | D3DFVF_TEX1;

    struct ScreenVertex {
        float x, y, z, rhw;
        float u, v;
    };

    // What the two drivers that overload these render states take as "leave alpha alone".
    constexpr DWORD kAlphaToCoverageOffAmd = MAKEFOURCC('A', '2', 'M', '0');
    constexpr DWORD kAlphaToCoverageOffNvidia = D3DFMT_UNKNOWN;
}

AimingOverhaul::ScreenDraw::ScreenDraw(IDirect3DDevice9* device, UINT firstConstant,
                                       UINT constantCount)
    : m_device(device), m_firstConstant(firstConstant),
      m_constantCount(constantCount < kMaxConstantRegisters ? constantCount
                                                            : kMaxConstantRegisters) {
    m_device->GetRenderTarget(0, &m_target);
    m_device->GetDepthStencilSurface(&m_depth);
    m_device->GetScissorRect(&m_scissor);
    m_device->GetViewport(&m_viewport);
    m_device->GetVertexShader(&m_vertexShader);
    m_device->GetPixelShader(&m_pixelShader);
    m_device->GetVertexDeclaration(&m_vertexDeclaration);
    m_device->GetStreamSource(0, &m_stream, &m_streamOffset, &m_streamStride);
    m_device->GetIndices(&m_indices);
    m_device->GetFVF(&m_vertexFormat);
    m_device->GetPixelShaderConstantF(m_firstConstant, m_pixelConstants, m_constantCount);
    for (size_t i = 0; i < kRenderStateCount; i++) {
        m_device->GetRenderState(kRenderStates[i], &m_renderStates[i]);
    }
    for (size_t i = 0; i < kStageStateCount; i++) {
        m_device->GetTextureStageState(0, kStageStates[i], &m_stageStates[i]);
    }
    for (DWORD sampler = 0; sampler < kSamplers; sampler++) {
        for (size_t i = 0; i < kSamplerStateCount; i++) {
            m_device->GetSamplerState(sampler, kSamplerStates[i], &m_samplerStates[sampler][i]);
        }
        m_device->GetTexture(sampler, &m_textures[sampler]);
    }

    m_device->SetVertexShader(nullptr);
    m_device->SetRenderState(D3DRS_ZENABLE, D3DZB_FALSE);
    m_device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
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
    m_device->SetRenderState(D3DRS_DEPTHBIAS, 0);
    m_device->SetRenderState(D3DRS_SLOPESCALEDEPTHBIAS, 0);
    m_device->SetRenderState(D3DRS_MULTISAMPLEANTIALIAS, TRUE);
    m_device->SetRenderState(D3DRS_MULTISAMPLEMASK, 0xFFFFFFFF);
    m_device->SetRenderState(D3DRS_POINTSIZE, kAlphaToCoverageOffAmd);
    m_device->SetRenderState(D3DRS_ADAPTIVETESS_Y, kAlphaToCoverageOffNvidia);
    m_device->SetRenderState(D3DRS_COLORWRITEENABLE,
                             D3DCOLORWRITEENABLE_RED | D3DCOLORWRITEENABLE_GREEN |
                                 D3DCOLORWRITEENABLE_BLUE | D3DCOLORWRITEENABLE_ALPHA);
    m_device->SetTextureStageState(0, D3DTSS_TEXCOORDINDEX, 0);
    m_device->SetTextureStageState(0, D3DTSS_TEXTURETRANSFORMFLAGS, D3DTTFF_DISABLE);
    for (DWORD sampler = 0; sampler < kSamplers; sampler++) {
        m_device->SetSamplerState(sampler, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
        m_device->SetSamplerState(sampler, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
        m_device->SetSamplerState(sampler, D3DSAMP_ADDRESSW, D3DTADDRESS_CLAMP);
        m_device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
        m_device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_POINT);
        m_device->SetSamplerState(sampler, D3DSAMP_MIPFILTER, D3DTEXF_NONE);
        m_device->SetSamplerState(sampler, D3DSAMP_SRGBTEXTURE, FALSE);
        m_device->SetSamplerState(sampler, D3DSAMP_MIPMAPLODBIAS, 0);
        m_device->SetSamplerState(sampler, D3DSAMP_MAXMIPLEVEL, 0);
    }

    D3DVIEWPORT9 wholeRange = m_viewport;
    wholeRange.MinZ = 0.0f;
    wholeRange.MaxZ = 1.0f;
    m_device->SetViewport(&wholeRange);
}

AimingOverhaul::ScreenDraw::~ScreenDraw() {
    // Switching targets resets the viewport and the scissor, so they go back after it.
    m_device->SetRenderTarget(0, m_target);
    m_device->SetDepthStencilSurface(m_depth);
    m_device->SetScissorRect(&m_scissor);
    m_device->SetViewport(&m_viewport);

    m_device->SetVertexShader(m_vertexShader);
    m_device->SetPixelShader(m_pixelShader);
    // With a declaration bound GetFVF reports zero, and restoring that would unbind it.
    if (m_vertexFormat != 0) {
        m_device->SetFVF(m_vertexFormat);
    }
    m_device->SetVertexDeclaration(m_vertexDeclaration);
    m_device->SetStreamSource(0, m_stream, m_streamOffset, m_streamStride);
    m_device->SetIndices(m_indices);
    m_device->SetPixelShaderConstantF(m_firstConstant, m_pixelConstants, m_constantCount);

    for (size_t i = 0; i < kRenderStateCount; i++) {
        m_device->SetRenderState(kRenderStates[i], m_renderStates[i]);
    }
    for (size_t i = 0; i < kStageStateCount; i++) {
        m_device->SetTextureStageState(0, kStageStates[i], m_stageStates[i]);
    }
    for (DWORD sampler = 0; sampler < kSamplers; sampler++) {
        for (size_t i = 0; i < kSamplerStateCount; i++) {
            m_device->SetSamplerState(sampler, kSamplerStates[i], m_samplerStates[sampler][i]);
        }
        m_device->SetTexture(sampler, m_textures[sampler]);
        Release(m_textures[sampler]);
    }

    Release(m_target);
    Release(m_depth);
    Release(m_vertexShader);
    Release(m_pixelShader);
    Release(m_vertexDeclaration);
    Release(m_stream);
    Release(m_indices);
}

void AimingOverhaul::ScreenDraw::Quad(float left, float top, float right, float bottom) {
    const ScreenVertex quad[4] = {
        {left - 0.5f, top - 0.5f, 0.0f, 1.0f, 0.0f, 0.0f},
        {right - 0.5f, top - 0.5f, 0.0f, 1.0f, 1.0f, 0.0f},
        {left - 0.5f, bottom - 0.5f, 0.0f, 1.0f, 0.0f, 1.0f},
        {right - 0.5f, bottom - 0.5f, 0.0f, 1.0f, 1.0f, 1.0f},
    };
    m_device->SetFVF(kVertexFormat);
    m_device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(ScreenVertex));
}

void AimingOverhaul::ScreenDraw::Triangles(const Vertex* vertices, UINT count) {
    struct PlacedVertex {
        float x, y, z, rhw;
        float u, v;
        float s, t;
    };
    std::vector<PlacedVertex> placed;
    placed.reserve(count);
    for (UINT i = 0; i < count; i++) {
        const Vertex& vertex = vertices[i];
        placed.push_back({vertex.x - 0.5f, vertex.y - 0.5f, 0.0f, 1.0f, vertex.u, vertex.v,
                          vertex.s, vertex.t});
    }
    m_device->SetFVF(D3DFVF_XYZRHW | D3DFVF_TEX2);
    m_device->DrawPrimitiveUP(D3DPT_TRIANGLELIST, count / 3, placed.data(), sizeof(PlacedVertex));
}

void AimingOverhaul::ScreenDraw::Separable(UINT step, const float (&across)[4],
                                           const float (&down)[4], IDirect3DTexture9* from,
                                           const Target& scratch, IDirect3DSurface9* into,
                                           float width, float height) {
    m_device->SetPixelShaderConstantF(step, across, 1);
    m_device->SetRenderTarget(0, scratch.surface);
    m_device->SetTexture(0, from);
    Quad(0.0f, 0.0f, width, height);
    m_device->SetPixelShaderConstantF(step, down, 1);
    m_device->SetRenderTarget(0, into);
    m_device->SetTexture(0, scratch.texture);
    Quad(0.0f, 0.0f, width, height);
}

void AimingOverhaul::ScreenDraw::Linear(DWORD sampler) {
    m_device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    m_device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
}

void AimingOverhaul::ScreenDraw::KeepAlpha() {
    m_device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                         D3DCOLORWRITEENABLE_GREEN |
                                                         D3DCOLORWRITEENABLE_BLUE);
}
