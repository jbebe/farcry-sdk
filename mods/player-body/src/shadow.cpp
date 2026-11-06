// The player's shadow. See shadow.h.
#include "shadow.h"

#include "body.h"
#include "engine/pawn.h"
#include "engine/scene_depth.h"
#include "engine/seqlock.h"
#include "engine/sky_pass.h"
#include "engine/sun.h"
#include "fcse_api.h"

#include "shadow_ps.h"

#include <algorithm>
#include <atomic>
#include <cstring>
#include <iterator>
#include <vector>

namespace {
    namespace Pawn = PlayerBody::Pawn;
    namespace SkyPass = PlayerBody::SkyPass;
    namespace Sun = PlayerBody::Sun;

    // The column standing in for the body, in metres: each sphere's radius, and how far the top one
    // stands above the eye.
    constexpr float kRadius = 0.22f;
    constexpr float kCrown = 0.05f;
    // Sky passes with the player in first person on foot and no cloud layer submitted, before that
    // is logged.
    constexpr uint32_t kPatience = 300;

    // The lowest and highest spheres' centres in the world, z up.
    struct Column {
        bool cast;
        float feet[3];
        float head[3];
    };
    PlayerBody::Seqlock<Column> g_column;
    std::atomic<bool> g_enabled{true};

    // Render thread: the shader, the device it was made on and whether that device refused it; the
    // cloud layers submitted by the last pass, and the passes that saw none.
    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DPixelShader9* g_shader = nullptr;
    bool g_refused = false;
    uint32_t g_submissions = 0;
    uint32_t g_unsubmitted = 0;

    struct Vertex {
        float x, y, z, rhw;
        float u, v;
    };

    IDirect3DPixelShader9* Shader(IDirect3DDevice9* device) {
        if (device != g_owner) {
            if (g_shader != nullptr) {
                g_shader->Release();
                g_shader = nullptr;
            }
            g_owner = device;
            g_refused = false;
        }
        if (g_shader == nullptr && !g_refused) {
            // fxc writes bytes, and the device wants whole tokens.
            std::vector<DWORD> tokens(sizeof(g_shadowPixelShader) / sizeof(DWORD));
            std::memcpy(tokens.data(), g_shadowPixelShader, tokens.size() * sizeof(DWORD));
            g_refused = FAILED(device->CreatePixelShader(tokens.data(), &g_shader));
            if (g_refused) {
                FCSE::ApiPointer()->Log("shadow: this device refused the shader");
            }
        }
        return g_shader;
    }

    void Draw(const SkyPass::Pass& pass, IDirect3DTexture9* depth, IDirect3DPixelShader9* shader,
              const Column& column, const Sun::Light& light, float darkness) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DStateBlock9* saved = nullptr;
        if (FAILED(device->CreateStateBlock(D3DSBT_ALL, &saved))) {
            return;
        }
        const SkyPass::View& view = pass.view;
        const float* eye = view.eye;
        const float constants[] = {
            view.halfWidth, view.halfHeight, view.viewDistance, 0.0f,
            view.right[0], view.right[1], view.right[2], 0.0f,
            view.up[0], view.up[1], view.up[2], 0.0f,
            view.forward[0], view.forward[1], view.forward[2], 0.0f,
            column.feet[0] - eye[0], column.feet[1] - eye[1], column.feet[2] - eye[2], kRadius,
            column.head[0] - eye[0], column.head[1] - eye[1], column.head[2] - eye[2], darkness,
            light.direction[0], light.direction[1], light.direction[2], 0.0f,
        };
        device->SetPixelShaderConstantF(0, constants, static_cast<UINT>(std::size(constants) / 4));
        device->SetPixelShader(shader);
        device->SetVertexShader(nullptr);
        device->SetFVF(D3DFVF_XYZRHW | D3DFVF_TEX1);
        device->SetTexture(0, depth);
        device->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
        device->SetSamplerState(0, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_POINT);
        device->SetSamplerState(0, D3DSAMP_MIPFILTER, D3DTEXF_NONE);
        device->SetSamplerState(0, D3DSAMP_SRGBTEXTURE, FALSE);
        device->SetTextureStageState(0, D3DTSS_TEXCOORDINDEX, 0);
        device->SetTextureStageState(0, D3DTSS_TEXTURETRANSFORMFLAGS, D3DTTFF_DISABLE);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_FALSE);
        device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
        device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
        device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
        device->SetRenderState(D3DRS_FOGENABLE, FALSE);
        device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, 0);
        device->SetRenderState(D3DRS_SRGBWRITEENABLE, FALSE);
        device->SetRenderState(D3DRS_ALPHATESTENABLE, TRUE);
        device->SetRenderState(D3DRS_ALPHAFUNC, D3DCMP_GREATER);
        device->SetRenderState(D3DRS_ALPHAREF, 0);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_BLENDOP, D3DBLENDOP_ADD);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
        // Colour only: the target's alpha is the bloom's brightness.
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);

        // Edges half a pixel back, so each pixel reads the depth texel under its centre.
        const D3DVIEWPORT9& viewport = pass.viewport;
        const float left = static_cast<float>(viewport.X) - 0.5f;
        const float top = static_cast<float>(viewport.Y) - 0.5f;
        const float right = left + static_cast<float>(viewport.Width);
        const float bottom = top + static_cast<float>(viewport.Height);
        const Vertex quad[4] = {
            {left, top, 0.0f, 1.0f, 0.0f, 0.0f},
            {right, top, 0.0f, 1.0f, 1.0f, 0.0f},
            {left, bottom, 0.0f, 1.0f, 0.0f, 1.0f},
            {right, bottom, 0.0f, 1.0f, 1.0f, 1.0f},
        };
        device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(Vertex));

        saved->Apply();
        saved->Release();
    }

    void OnSkyPass(const SkyPass::Pass& pass) {
        // A world was drawn since the last pass only if a cloud layer was submitted for it.
        const uint32_t submissions = Sun::Submissions();
        const bool live = submissions != g_submissions;
        g_submissions = submissions;
        Column column;
        if (!g_column.Latest(column) || !column.cast) {
            return;
        }
        if (submissions == 0 && ++g_unsubmitted == kPatience) {
            FCSE::ApiPointer()->Log("shadow: no cloud layer has reached this plugin, so no shadow is "
                                    "drawn; another plugin may be holding the submission back");
        }
        Sun::Light light;
        IDirect3DTexture9* depth = PlayerBody::SceneDepth::Texture();
        if (!live || depth == nullptr || !Sun::Latest(light)) {
            return;
        }
        const float darkness = (1.0f - light.night) * (1.0f - light.storm);
        IDirect3DPixelShader9* shader = darkness > 0.0f ? Shader(pass.device) : nullptr;
        if (shader != nullptr) {
            Draw(pass, depth, shader, column, light, darkness);
        }
    }
}

bool PlayerBody::Shadow::Install() {
    return Sun::Install() && SceneDepth::Install() && SkyPass::Install(&OnSkyPass);
}

void PlayerBody::Shadow::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void PlayerBody::Shadow::Update(uint8_t* pawn) {
    Column column = {};
    column.cast = g_enabled && Pawn::FirstPerson(pawn) && Pawn::OnFoot(pawn) &&
                  Body::Eye(pawn, column.head);
    if (column.cast) {
        std::copy_n(Pawn::Place(Pawn::Entity(pawn)), 3, column.feet);
        column.feet[2] += kRadius;
        column.head[2] += kCrown;
    }
    g_column.Publish(column);
}
