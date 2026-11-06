// The player's shadow. See shadow.h.
#include "shadow.h"

#include "engine/com.h"
#include "engine/pawn.h"
#include "engine/scene_depth.h"
#include "engine/seqlock.h"
#include "engine/sky_pass.h"
#include "fcse_api.h"

#include "shadow_ps.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <iterator>
#include <vector>

namespace {
    namespace Pawn = PlayerBody::Pawn;
    namespace SkyPass = PlayerBody::SkyPass;

    // In metres, the farthest the shader's circle reaches from the feet: OUTER across, HEIGHT up.
    constexpr float kReach = 0.75f;

    // Where the feet stand in the world, z up, while they cast the shadow.
    struct Feet {
        bool cast;
        float place[3];
    };
    PlayerBody::Seqlock<Feet> g_feet;
    std::atomic<bool> g_enabled{true};

    // Render thread: the shader, the device it was made on, and whether that device refused it.
    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DPixelShader9* g_shader = nullptr;
    bool g_refused = false;

    struct Vertex {
        float x, y, z, rhw;
        float u, v;
    };

    IDirect3DPixelShader9* Shader(IDirect3DDevice9* device) {
        if (device != g_owner) {
            PlayerBody::Release(g_shader);
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
              const float feet[3]) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DStateBlock9* saved = nullptr;
        if (FAILED(device->CreateStateBlock(D3DSBT_ALL, &saved))) {
            return;
        }
        const SkyPass::View& view = pass.view;
        const float constants[] = {
            view.halfWidth, view.halfHeight, view.viewDistance, 0.0f,
            view.right[0], view.right[1], view.right[2], 0.0f,
            view.up[0], view.up[1], view.up[2], 0.0f,
            view.forward[0], view.forward[1], view.forward[2], 0.0f,
            feet[0], feet[1], feet[2], 0.0f,
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

    // Whether everything within kReach of `centre`, from the eye, lies outside the view: behind the
    // eye, or past one of its four edges, each a plane through the eye.
    bool OutOfView(const SkyPass::View& view, const float centre[3]) {
        const auto along = [centre](const float axis[3]) {
            return centre[0] * axis[0] + centre[1] * axis[1] + centre[2] * axis[2];
        };
        const float ahead = along(view.forward);
        const auto past = [ahead](float off, float half) {
            return (ahead * half - off) / std::sqrt(1.0f + half * half) < -kReach;
        };
        const float across = along(view.right);
        const float above = along(view.up);
        return ahead < -kReach || past(across, view.halfWidth) || past(-across, view.halfWidth) ||
               past(above, view.halfHeight) || past(-above, view.halfHeight);
    }

    void OnSkyPass(const SkyPass::Pass& pass) {
        Feet feet;
        IDirect3DTexture9* depth = PlayerBody::SceneDepth::Texture();
        if (depth == nullptr || !g_feet.Latest(feet) || !feet.cast) {
            return;
        }
        const float* eye = pass.view.eye;
        const float fromEye[3] = {feet.place[0] - eye[0], feet.place[1] - eye[1],
                                  feet.place[2] - eye[2]};
        IDirect3DPixelShader9* shader = OutOfView(pass.view, fromEye) ? nullptr : Shader(pass.device);
        if (shader != nullptr) {
            Draw(pass, depth, shader, fromEye);
        }
    }
}

bool PlayerBody::Shadow::Install() {
    return SceneDepth::Install() && SkyPass::Install(&OnSkyPass);
}

void PlayerBody::Shadow::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void PlayerBody::Shadow::Update(uint8_t* pawn) {
    Feet feet = {};
    uint8_t* entity = Pawn::Entity(pawn);
    feet.cast = g_enabled && entity != nullptr && Pawn::FirstPerson(pawn) && Pawn::OnFoot(pawn);
    if (feet.cast) {
        std::copy_n(Pawn::Place(entity), 3, feet.place);
    }
    g_feet.Publish(feet);
}
