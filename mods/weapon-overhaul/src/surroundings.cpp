// The second view is shrunk to half its size and blurred across and down, as the other eye sees
// past the scope, before it is laid down outside the eyepiece.
#include "surroundings.h"

#include "engine/aim.h"
#include "engine/frame.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/second_view.h"
#include "engine/shader.h"
#include "eyepiece.h"
#include "fcse_api.h"

#include "blur_blur_ps.h"
#include "surroundings_ps.h"

#include <algorithm>
#include <atomic>

namespace {
    // How soft the surroundings are, the blur's sigma as a share of the screen's height, and the
    // sigma of BlurPS in its own steps.
    constexpr float kSoftness = 0.007f;
    constexpr float kBlurSigmaSteps = 3.0f;

    WeaponOverhaul::PixelShader g_shader{"surroundings", g_surroundingsPixelShader};
    WeaponOverhaul::PixelShader g_blurShader{"surroundings blur", g_blurPixelShader};

    std::atomic<bool> g_enabled{true};
    // The camera's field of view the last time the player was not aiming, in radians.
    float g_ownFov = 0.0f;

    // The second view at half its size, softened, and the target it is blurred across into, on the
    // device that made them; refused until a reset if the device would not.
    IDirect3DDevice9* g_owner = nullptr;
    WeaponOverhaul::Target g_soft;
    WeaponOverhaul::Target g_across;
    UINT g_width = 0;
    UINT g_height = 0;
    D3DFORMAT g_format = D3DFMT_UNKNOWN;
    bool g_refused = false;

    void ReleaseTargets() {
        WeaponOverhaul::Release(g_soft);
        WeaponOverhaul::Release(g_across);
    }

    bool EnsureTargets(IDirect3DDevice9* device, IDirect3DTexture9* view) {
        D3DSURFACE_DESC desc = {};
        view->GetLevelDesc(0, &desc);
        const UINT width = (std::max)(desc.Width / 2, 1u);
        const UINT height = (std::max)(desc.Height / 2, 1u);
        if (g_owner == device && g_soft.texture != nullptr && g_width == width &&
            g_height == height && g_format == desc.Format) {
            return true;
        }
        ReleaseTargets();
        if (g_refused) {
            return false;
        }
        g_owner = device;
        g_width = width;
        g_height = height;
        g_format = desc.Format;
        if (FAILED(WeaponOverhaul::CreateTarget(device, width, height, desc.Format, g_soft)) ||
            FAILED(WeaponOverhaul::CreateTarget(device, width, height, desc.Format, g_across))) {
            ReleaseTargets();
            g_refused = true;
            FCSE::ApiPointer()->Log("surroundings: the device refused the targets to blur them in");
            return false;
        }
        return true;
    }

    // The second view, shrunk and blurred across and down; null if it could not be.
    IDirect3DTexture9* Soften(IDirect3DDevice9* device, IDirect3DTexture9* view) {
        IDirect3DPixelShader9* blur = g_blurShader.Get(device);
        if (blur == nullptr || !EnsureTargets(device, view)) {
            return nullptr;
        }
        WeaponOverhaul::ScreenDraw draw(device, 1, 1);
        // A target of our own cannot share the scene's multisampled depth.
        device->SetDepthStencilSurface(nullptr);
        IDirect3DSurface9* source = nullptr;
        view->GetSurfaceLevel(0, &source);
        device->StretchRect(source, nullptr, g_soft.surface, nullptr, D3DTEXF_LINEAR);
        WeaponOverhaul::Release(source);

        // The view spans the screen, so a step is the same share of it either way.
        const float down = kSoftness / kBlurSigmaSteps;
        const float across[4] = {down * WeaponOverhaul::Frame::Height() /
                                     WeaponOverhaul::Frame::Width(),
                                 0.0f, 0.0f, 0.0f};
        const float downward[4] = {0.0f, down, 0.0f, 0.0f};
        const float width = static_cast<float>(g_width);
        const float height = static_cast<float>(g_height);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetPixelShader(blur);
        device->SetPixelShaderConstantF(1, across, 1);
        device->SetRenderTarget(0, g_across.surface);
        device->SetTexture(0, g_soft.texture);
        draw.Quad(0.0f, 0.0f, width, height);
        device->SetPixelShaderConstantF(1, downward, 1);
        device->SetRenderTarget(0, g_soft.surface);
        device->SetTexture(0, g_across.texture);
        draw.Quad(0.0f, 0.0f, width, height);
        return g_soft.texture;
    }
}

float WeaponOverhaul::Surroundings::Want(float cameraFov) {
    if (!Aim::Aiming()) {
        g_ownFov = cameraFov;
    }
    return g_enabled && Eyepiece::Expected() ? g_ownFov : 0.0f;
}

void WeaponOverhaul::Surroundings::BeforeColour(IDirect3DDevice9* device) {
    IDirect3DTexture9* view = SecondView::Latest();
    const std::optional<Eyepiece::Opening> opening = Eyepiece::Open();
    IDirect3DPixelShader9* shader =
        view != nullptr && opening && g_enabled ? g_shader.Get(device) : nullptr;
    IDirect3DTexture9* soft = shader != nullptr ? Soften(device, view) : nullptr;
    if (soft == nullptr) {
        return;
    }
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float constants[4] = {width / height, opening->x, opening->y, opening->radius};

    ScreenDraw draw(device, 0, 1);
    device->SetPixelShaderConstantF(0, constants, 1);
    device->SetTexture(0, soft);
    device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    // The scene's alpha carries brightness for the bloom, so it is left alone.
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShader(shader);
    draw.Quad(0.0f, 0.0f, width, height);
}

void WeaponOverhaul::Surroundings::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Surroundings::ReleaseDeviceObjects() {
    g_shader.Release();
    g_blurShader.Release();
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
}
