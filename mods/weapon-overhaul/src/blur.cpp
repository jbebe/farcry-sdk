// The gun out of focus down the iron sights, the eye focused on the front sight.
//
// The pass the gun's colour was drawn in is copied out, taken down to half size with each pixel's
// blur beside it, blurred across and then down, and blended back over the gun and around its edge.
#include "blur.h"

#include "engine/aim.h"
#include "engine/com.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/weapon_draws.h"
#include "fcse_api.h"

#include "blur_blur_ps.h"
#include "blur_composite_ps.h"
#include "blur_down_ps.h"

#include <atomic>
#include <cmath>

namespace {
    // The eye's pupil, in metres, which is how fast the blur grows away from the front sight.
    constexpr float kPupil = 0.004f;
    // The largest blur radius, and how far around the screen's centre the front sight is looked
    // for, both as a share of the screen's height.
    constexpr float kLargest = 0.012f;
    constexpr float kFocusReach = 0.025f;
    // BlurPS's steps either side.
    constexpr float kSteps = 6.0f;

    constexpr UINT kLens = 0;
    constexpr UINT kStep = 2;
    constexpr UINT kConstantCount = 3;

    constexpr D3DFORMAT kHalfFormat = D3DFMT_A16B16G16R16F;

    struct Target {
        IDirect3DTexture9* texture = nullptr;
        IDirect3DSurface9* surface = nullptr;
    };

    WeaponOverhaul::PixelShader g_downShader{"blur down", g_blurDownPixelShader};
    WeaponOverhaul::PixelShader g_blurShader{"blur", g_blurPixelShader};
    WeaponOverhaul::PixelShader g_compositeShader{"blur composite", g_blurCompositePixelShader};

    std::atomic<bool> g_enabled{true};
    IDirect3DDevice9* g_owner = nullptr;
    // The scene resolved, and at half size: taken down, blurred across, blurred both ways.
    Target g_copy;
    Target g_halves[3];
    D3DSURFACE_DESC g_copyDesc = {};
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;
    uint32_t g_drawnFrame = 0xFFFFFFFFu;
    bool g_reported = false;

    void ReleaseTarget(Target& target) {
        WeaponOverhaul::Release(target.surface);
        WeaponOverhaul::Release(target.texture);
    }

    void ReleaseTargets() {
        ReleaseTarget(g_copy);
        for (Target& half : g_halves) {
            ReleaseTarget(half);
        }
    }

    bool Create(IDirect3DDevice9* device, const D3DSURFACE_DESC& desc, Target& target) {
        const HRESULT created =
            WeaponOverhaul::CreateTarget(device, desc, &target.texture, &target.surface);
        if (FAILED(created)) {
            ReleaseTargets();
            g_refused = true;
            FCSE::Logf("blur: the device refused a %ux%u target, 0x%08lX", desc.Width, desc.Height,
                       static_cast<unsigned long>(created));
            return false;
        }
        return true;
    }

    bool EnsureTargets(IDirect3DDevice9* device, const D3DSURFACE_DESC& scene) {
        if (g_owner == device && g_copy.texture != nullptr && g_copyDesc.Width == scene.Width &&
            g_copyDesc.Height == scene.Height && g_copyDesc.Format == scene.Format) {
            return true;
        }
        ReleaseTargets();
        if (g_refused) {
            return false;
        }
        g_owner = device;
        g_copyDesc = scene;
        D3DSURFACE_DESC half = {};
        half.Width = (scene.Width + 1) / 2;
        half.Height = (scene.Height + 1) / 2;
        half.Format = kHalfFormat;
        bool made = Create(device, scene, g_copy);
        for (Target& target : g_halves) {
            made = made && Create(device, half, target);
        }
        return made;
    }

    bool Draw(const WeaponOverhaul::Frame::Pass& pass, const WeaponOverhaul::WeaponDraws::Depth& depth,
              float settled) {
        IDirect3DDevice9* device = pass.device;
        D3DSURFACE_DESC scene = {};
        pass.target->GetDesc(&scene);
        IDirect3DPixelShader9* down = g_downShader.Get(device);
        IDirect3DPixelShader9* blur = g_blurShader.Get(device);
        IDirect3DPixelShader9* composite = g_compositeShader.Get(device);
        if (down == nullptr || blur == nullptr || composite == nullptr ||
            !EnsureTargets(device, scene) ||
            FAILED(device->StretchRect(pass.target, nullptr, g_copy.surface, nullptr, D3DTEXF_NONE))) {
            return false;
        }

        const float width = static_cast<float>(scene.Width);
        const float height = static_cast<float>(scene.Height);
        const float halfWidth = static_cast<float>((scene.Width + 1) / 2);
        const float halfHeight = static_cast<float>((scene.Height + 1) / 2);
        // A blur of the largest radius in kSteps steps, in half-size pixels.
        const float step = kLargest * halfHeight / kSteps;
        // The blur's diameter is the pupil times the dioptres away from focus; a dioptre is the
        // stored depth over the projection's depth offset, and an angle on screen is the vertical
        // scale over two of the height.
        const float lens = kPupil * depth.verticalScale / (4.0f * std::abs(depth.depthOffset) * kLargest);
        const float constants[kConstantCount * 4] = {
            lens, settled, 0.0f, 0.0f,
            kFocusReach * height / width, kFocusReach, 0.0f, 0.0f,
            step / halfWidth, 0.0f, 0.0f, 0.0f,
        };

        WeaponOverhaul::ScreenDraw draw(device, kLens, kConstantCount);
        device->SetPixelShaderConstantF(kLens, constants, kConstantCount);
        for (DWORD sampler = 0; sampler < 3; sampler++) {
            device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
            device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        }
        device->SetTexture(3, depth.texture);
        // A half-size target cannot share the scene's multisampled depth.
        device->SetDepthStencilSurface(nullptr);

        device->SetRenderTarget(0, g_halves[0].surface);
        device->SetTexture(0, g_copy.texture);
        device->SetPixelShader(down);
        draw.Quad(0.0f, 0.0f, halfWidth, halfHeight);

        device->SetPixelShader(blur);
        device->SetRenderTarget(0, g_halves[1].surface);
        device->SetTexture(0, g_halves[0].texture);
        draw.Quad(0.0f, 0.0f, halfWidth, halfHeight);

        const float downward[4] = {0.0f, step / halfHeight, 0.0f, 0.0f};
        device->SetPixelShaderConstantF(kStep, downward, 1);
        device->SetRenderTarget(0, g_halves[2].surface);
        device->SetTexture(0, g_halves[1].texture);
        draw.Quad(0.0f, 0.0f, halfWidth, halfHeight);

        // The scene's alpha carries brightness for the bloom, so it is left alone.
        device->SetRenderTarget(0, pass.target);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetTexture(0, g_copy.texture);
        device->SetTexture(1, g_halves[2].texture);
        device->SetTexture(2, g_halves[0].texture);
        device->SetPixelShader(composite);
        draw.Quad(0.0f, 0.0f, width, height);
        return true;
    }
}

void WeaponOverhaul::Blur::OnScenePass(const Frame::Pass& pass) {
    const float settled = g_enabled ? Aim::Settled() : 0.0f;
    WeaponDraws::SetWatching(settled > 0.0f);
    if (settled <= 0.0f || pass.serial != WeaponDraws::ColourPass() ||
        g_drawnFrame == Frame::Number()) {
        return;
    }
    g_drawnFrame = Frame::Number();

    WeaponDraws::Depth depth = {};
    const bool found = WeaponDraws::Latest(depth);
    const bool drawn = found && Draw(pass, depth, settled);
    if (!g_reported) {
        g_reported = true;
        if (drawn) {
            FCSE::Logf("blur: drawing, the gun's vertical scale %.3f and depth offset %.4f",
                       depth.verticalScale, depth.depthOffset);
        } else {
            FCSE::Logf("blur: the gun's colour pass was found but %s",
                       found ? "the blur could not draw" : "not its depth");
        }
    }
}

void WeaponOverhaul::Blur::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Blur::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
    g_downShader.Release();
    g_blurShader.Release();
    g_compositeShader.Release();
}
