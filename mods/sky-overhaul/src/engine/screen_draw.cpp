#include "engine/screen_draw.h"

#include "engine/com.h"

namespace {
    constexpr DWORD kVertexFormat = D3DFVF_XYZRHW | D3DFVF_TEX1;

    struct ScreenVertex {
        float x, y, z, rhw;
        float u, v;
    };

    struct RayVertex {
        float x, y, z, w;
        float ray[3];
    };

    // What the two drivers that overload these render states take as "leave alpha alone".
    constexpr DWORD kAlphaToCoverageOffAmd = MAKEFOURCC('A', '2', 'M', '0');
    constexpr DWORD kAlphaToCoverageOffNvidia = D3DFMT_UNKNOWN;

    // One per device, since a vertex declaration outlives a reset and only a new device needs a
    // new one.
    IDirect3DDevice9* g_declarationOwner = nullptr;
    IDirect3DVertexDeclaration9* g_rayDeclaration = nullptr;

    IDirect3DVertexDeclaration9* RayDeclaration(IDirect3DDevice9* device) {
        if (g_declarationOwner != device) {
            SkyOverhaul::Release(g_rayDeclaration);
            g_declarationOwner = device;
        }
        if (g_rayDeclaration == nullptr) {
            const D3DVERTEXELEMENT9 elements[] = {
                {0, 0, D3DDECLTYPE_FLOAT4, D3DDECLMETHOD_DEFAULT, D3DDECLUSAGE_POSITION, 0},
                {0, 16, D3DDECLTYPE_FLOAT3, D3DDECLMETHOD_DEFAULT, D3DDECLUSAGE_TEXCOORD, 0},
                D3DDECL_END()};
            device->CreateVertexDeclaration(elements, &g_rayDeclaration);
        }
        return g_rayDeclaration;
    }
}

SkyOverhaul::DrawGuard::DrawGuard(IDirect3DDevice9* device, UINT firstConstant, UINT constantCount)
    : m_device(device), m_firstConstant(firstConstant),
      m_constantCount(constantCount > kMaxConstantRegisters ? kMaxConstantRegisters
                                                            : constantCount) {
    m_device->GetVertexShader(&m_vertexShader);
    m_device->GetPixelShader(&m_pixelShader);
    m_device->GetVertexDeclaration(&m_vertexDeclaration);
    m_device->GetStreamSource(0, &m_stream, &m_streamOffset, &m_streamStride);
    m_device->GetIndices(&m_indices);
    m_device->GetFVF(&m_vertexFormat);
    m_device->GetPixelShaderConstantF(m_firstConstant, m_pixelConstants, m_constantCount);
}

SkyOverhaul::DrawGuard::~DrawGuard() {
    m_device->SetVertexShader(m_vertexShader);
    m_device->SetPixelShader(m_pixelShader);

    // The declaration last and the format only if there was one: with a declaration bound GetFVF
    // reports zero, so restoring a zero format over a live declaration would unbind it.
    if (m_vertexFormat != 0) {
        m_device->SetFVF(m_vertexFormat);
    }
    m_device->SetVertexDeclaration(m_vertexDeclaration);

    // DrawPrimitiveUP leaves stream zero unbound, and an engine that filters redundant binds would
    // then draw nothing for the rest of the frame.
    m_device->SetStreamSource(0, m_stream, m_streamOffset, m_streamStride);
    m_device->SetIndices(m_indices);
    m_device->SetPixelShaderConstantF(m_firstConstant, m_pixelConstants, m_constantCount);

    Release(m_vertexShader);
    Release(m_pixelShader);
    Release(m_vertexDeclaration);
    Release(m_stream);
    Release(m_indices);
}

void SkyOverhaul::DrawGuard::Quad(float left, float top, float right, float bottom, float depth) {
    const ScreenVertex quad[4] = {
        {left - 0.5f, top - 0.5f, depth, 1.0f, 0.0f, 0.0f},
        {right - 0.5f, top - 0.5f, depth, 1.0f, 1.0f, 0.0f},
        {left - 0.5f, bottom - 0.5f, depth, 1.0f, 0.0f, 1.0f},
        {right - 0.5f, bottom - 0.5f, depth, 1.0f, 1.0f, 1.0f},
    };
    m_device->SetFVF(kVertexFormat);
    m_device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(ScreenVertex));
}

bool SkyOverhaul::DrawGuard::ClipQuad(float depth, const float corners[4][3]) {
    IDirect3DVertexDeclaration9* declaration = RayDeclaration(m_device);
    if (declaration == nullptr) {
        return false;
    }

    // Clip space with w of one, so the corners land on the viewport's edges whatever its size and
    // the interpolation across them stays linear. No half-pixel shift: that rule is for reading a
    // texture by screen position, and here each corner's direction belongs at the viewport's edge,
    // which is exactly where a rasteriser interpolating to a pixel centre expects it.
    const RayVertex quad[4] = {
        {-1.0f, 1.0f, depth, 1.0f, {corners[0][0], corners[0][1], corners[0][2]}},
        {1.0f, 1.0f, depth, 1.0f, {corners[1][0], corners[1][1], corners[1][2]}},
        {-1.0f, -1.0f, depth, 1.0f, {corners[2][0], corners[2][1], corners[2][2]}},
        {1.0f, -1.0f, depth, 1.0f, {corners[3][0], corners[3][1], corners[3][2]}},
    };
    m_device->SetVertexDeclaration(declaration);
    m_device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(RayVertex));
    return true;
}

void SkyOverhaul::DrawGuard::ReleaseDeviceObjects() {
    Release(g_rayDeclaration);
    g_declarationOwner = nullptr;
}

SkyOverhaul::ScreenDraw::ScreenDraw(IDirect3DDevice9* device, UINT firstConstant,
                                    UINT constantCount)
    : DrawGuard(device, firstConstant, constantCount) {
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
    m_device->GetViewport(&m_viewport);

    m_device->SetVertexShader(nullptr);
    m_device->SetPixelShader(nullptr);

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
    m_device->SetRenderState(D3DRS_DEPTHBIAS, 0);
    m_device->SetRenderState(D3DRS_SLOPESCALEDEPTHBIAS, 0);
    m_device->SetRenderState(D3DRS_MULTISAMPLEANTIALIAS, TRUE);
    m_device->SetRenderState(D3DRS_MULTISAMPLEMASK, 0xFFFFFFFF);
    m_device->SetRenderState(D3DRS_POINTSIZE, kAlphaToCoverageOffAmd);
    m_device->SetRenderState(D3DRS_ADAPTIVETESS_Y, kAlphaToCoverageOffNvidia);
    m_device->SetRenderState(D3DRS_COLORWRITEENABLE,
                             D3DCOLORWRITEENABLE_RED | D3DCOLORWRITEENABLE_GREEN |
                                 D3DCOLORWRITEENABLE_BLUE | D3DCOLORWRITEENABLE_ALPHA);

    m_device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
    m_device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_TEXTURE);
    m_device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
    m_device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_TEXTURE);
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

    // A screen draw takes the whole depth range rather than whatever range the pass it interrupts
    // was using, which would otherwise remap the quad's own depth along with everything else.
    D3DVIEWPORT9 wholeRange = m_viewport;
    wholeRange.MinZ = 0.0f;
    wholeRange.MaxZ = 1.0f;
    m_device->SetViewport(&wholeRange);
}

SkyOverhaul::ScreenDraw::~ScreenDraw() {
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
    m_device->SetViewport(&m_viewport);
}
