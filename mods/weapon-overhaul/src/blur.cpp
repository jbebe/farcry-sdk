// The gun out of focus down the iron sights, the eye focused on the front sight.
//
// The pass the gun's colour was drawn in is copied out. The focus is found on the front sight and
// eased toward, the gun's colour taken down to half size weighted by how blurred each part is, that
// blurred a little and a lot, and the result blended back over the gun and around its edge.
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
#include "blur_focus_ps.h"

#include <atomic>
#include <chrono>
#include <cmath>

namespace {
    // The eye's pupil, in metres, which is how fast the blur grows away from the front sight.
    constexpr float kPupil = 0.008f;
    // The largest blur radius, as a share of the screen's height, and the small one, as a share of
    // the largest.
    constexpr float kLargest = 0.012f;
    constexpr float kSmall = 1.0f / 3.0f;
    // BlurPS's steps either side.
    constexpr float kSteps = 6.0f;
    // Seconds for the eye to refocus most of the way.
    constexpr float kRefocus = 0.1f;

    constexpr UINT kLens = 0;
    constexpr UINT kStep = 1;
    constexpr UINT kConstantCount = 4;

    constexpr D3DFORMAT kHalfFormat = D3DFMT_A16B16G16R16F;
    constexpr D3DFORMAT kFocusFormat = D3DFMT_A32B32G32R32F;

    struct Target {
        IDirect3DTexture9* texture = nullptr;
        IDirect3DSurface9* surface = nullptr;
    };

    WeaponOverhaul::PixelShader g_focusShader{"blur focus", g_blurFocusPixelShader};
    WeaponOverhaul::PixelShader g_downShader{"blur down", g_blurDownPixelShader};
    WeaponOverhaul::PixelShader g_blurShader{"blur", g_blurPixelShader};
    WeaponOverhaul::PixelShader g_compositeShader{"blur composite", g_blurCompositePixelShader};

    std::atomic<bool> g_enabled{true};
    IDirect3DDevice9* g_owner = nullptr;
    // The scene resolved; the focus, this frame's and the last; and at half size the gun taken
    // down, a scratch target, and the gun blurred a little and a lot.
    Target g_copy;
    Target g_focus[2];
    Target g_halves[4];
    D3DSURFACE_DESC g_copyDesc = {};
    // Which focus target this frame writes, and whether both still hold whatever they were made with.
    int g_focusIndex = 0;
    bool g_focusFresh = true;
    std::chrono::steady_clock::time_point g_lastDraw;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;
    uint32_t g_drawnFrame = 0xFFFFFFFFu;
    bool g_reported = false;

    IDirect3DSurface9* g_readback = nullptr;
    WeaponOverhaul::WeaponDraws::Depth g_lastDepth = {};

    void ReleaseTarget(Target& target) {
        WeaponOverhaul::Release(target.surface);
        WeaponOverhaul::Release(target.texture);
    }

    void ReleaseTargets() {
        ReleaseTarget(g_copy);
        for (Target& focus : g_focus) {
            ReleaseTarget(focus);
        }
        for (Target& half : g_halves) {
            ReleaseTarget(half);
        }
        g_focusFresh = true;
    }

    bool Create(IDirect3DDevice9* device, UINT width, UINT height, D3DFORMAT format,
                Target& target) {
        D3DSURFACE_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.Format = format;
        const HRESULT created =
            WeaponOverhaul::CreateTarget(device, desc, &target.texture, &target.surface);
        if (FAILED(created)) {
            ReleaseTargets();
            g_refused = true;
            FCSE::Logf("blur: the device refused a %ux%u target of format %d, 0x%08lX", width,
                       height, format, static_cast<unsigned long>(created));
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
        bool made = Create(device, scene.Width, scene.Height, scene.Format, g_copy);
        for (Target& focus : g_focus) {
            made = made && Create(device, 1, 1, kFocusFormat, focus);
        }
        for (Target& half : g_halves) {
            made = made && Create(device, (scene.Width + 1) / 2, (scene.Height + 1) / 2,
                                  kHalfFormat, half);
        }
        return made;
    }

    // How far the focus moves toward this frame's, from the time since the last.
    float Refocus() {
        const auto now = std::chrono::steady_clock::now();
        const float seconds = std::chrono::duration<float>(now - g_lastDraw).count();
        g_lastDraw = now;
        return 1.0f - std::exp(-seconds / kRefocus);
    }

    // One Gaussian over the half-size gun, across and then down, `step` half-size pixels apart.
    void BlurInto(IDirect3DDevice9* device, WeaponOverhaul::ScreenDraw& draw, const Target& into,
                  float step, float halfWidth, float halfHeight) {
        const float across[4] = {step / halfWidth, 0.0f, 0.0f, 0.0f};
        const float downward[4] = {0.0f, step / halfHeight, 0.0f, 0.0f};
        device->SetPixelShaderConstantF(kStep, across, 1);
        device->SetRenderTarget(0, g_halves[1].surface);
        device->SetTexture(0, g_halves[0].texture);
        draw.Quad(0.0f, 0.0f, halfWidth, halfHeight);
        device->SetPixelShaderConstantF(kStep, downward, 1);
        device->SetRenderTarget(0, into.surface);
        device->SetTexture(0, g_halves[1].texture);
        draw.Quad(0.0f, 0.0f, halfWidth, halfHeight);
    }

    bool Draw(const WeaponOverhaul::Frame::Pass& pass,
              const WeaponOverhaul::WeaponDraws::Depth& depth, float settled) {
        IDirect3DDevice9* device = pass.device;
        D3DSURFACE_DESC scene = {};
        pass.target->GetDesc(&scene);
        IDirect3DPixelShader9* focus = g_focusShader.Get(device);
        IDirect3DPixelShader9* down = g_downShader.Get(device);
        IDirect3DPixelShader9* blur = g_blurShader.Get(device);
        IDirect3DPixelShader9* composite = g_compositeShader.Get(device);
        if (focus == nullptr || down == nullptr || blur == nullptr || composite == nullptr ||
            !EnsureTargets(device, scene) ||
            FAILED(device->StretchRect(pass.target, nullptr, g_copy.surface, nullptr,
                                       D3DTEXF_NONE))) {
            return false;
        }

        const float width = static_cast<float>(scene.Width);
        const float height = static_cast<float>(scene.Height);
        const float halfWidth = static_cast<float>((scene.Width + 1) / 2);
        const float halfHeight = static_cast<float>((scene.Height + 1) / 2);
        // The largest blur in kSteps steps, in half-size pixels.
        const float step = kLargest * halfHeight / kSteps;
        // The blur's diameter is the pupil times the dioptres from focus; dioptres are stored depth
        // over the depth offset, and an angle covers the vertical scale over two of the height.
        const float lens =
            kPupil * depth.verticalScale / (4.0f * std::abs(depth.depthOffset) * kLargest);
        const float constants[kConstantCount * 4] = {
            lens, settled, 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 0.0f,
            Refocus(), 0.0f, 0.0f, 0.0f,
            1.0f / halfWidth, 1.0f / halfHeight, 0.0f, 0.0f,
        };
        const Target& focusNow = g_focus[g_focusIndex];
        const Target& focusBefore = g_focus[g_focusIndex ^ 1];

        WeaponOverhaul::ScreenDraw draw(device, kLens, kConstantCount);
        device->SetPixelShaderConstantF(kLens, constants, kConstantCount);
        for (DWORD sampler : {0u, 1u, 2u, 4u}) {
            device->SetSamplerState(sampler, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
            device->SetSamplerState(sampler, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        }
        // A half-size target cannot share the scene's multisampled depth.
        device->SetDepthStencilSurface(nullptr);

        if (g_focusFresh) {
            for (const Target& target : g_focus) {
                device->SetRenderTarget(0, target.surface);
                device->Clear(0, nullptr, D3DCLEAR_TARGET, 0, 1.0f, 0);
            }
            g_focusFresh = false;
        }
        device->SetRenderTarget(0, focusNow.surface);
        device->SetTexture(3, depth.texture);
        device->SetTexture(5, focusBefore.texture);
        device->SetPixelShader(focus);
        draw.Quad(0.0f, 0.0f, 1.0f, 1.0f);

        device->SetRenderTarget(0, g_halves[0].surface);
        device->SetTexture(0, g_copy.texture);
        device->SetTexture(5, focusNow.texture);
        device->SetPixelShader(down);
        draw.Quad(0.0f, 0.0f, halfWidth, halfHeight);

        device->SetPixelShader(blur);
        BlurInto(device, draw, g_halves[2], step * kSmall, halfWidth, halfHeight);
        BlurInto(device, draw, g_halves[3], step, halfWidth, halfHeight);

        // The scene's alpha carries brightness for the bloom, so it is left alone.
        device->SetRenderTarget(0, pass.target);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetTexture(0, g_copy.texture);
        device->SetTexture(1, g_halves[2].texture);
        device->SetTexture(2, g_halves[3].texture);
        device->SetTexture(4, g_halves[0].texture);
        device->SetPixelShader(composite);
        draw.Quad(0.0f, 0.0f, width, height);

        g_focusIndex ^= 1;
        g_lastDepth = depth;
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

bool WeaponOverhaul::Blur::ReadFocus(IDirect3DDevice9* device, float metres[2]) {
    const Target& last = g_focus[g_focusIndex ^ 1];
    if (last.surface == nullptr || g_lastDepth.depthOffset == 0.0f) {
        return false;
    }
    if (g_readback == nullptr &&
        FAILED(device->CreateOffscreenPlainSurface(1, 1, kFocusFormat, D3DPOOL_SYSTEMMEM,
                                                   &g_readback, nullptr))) {
        return false;
    }
    D3DLOCKED_RECT locked = {};
    if (FAILED(device->GetRenderTargetData(last.surface, g_readback)) ||
        FAILED(g_readback->LockRect(&locked, nullptr, D3DLOCK_READONLY))) {
        return false;
    }
    const float* stored = static_cast<const float*>(locked.pBits);
    for (int i = 0; i < 2; i++) {
        metres[i] = std::abs(g_lastDepth.depthOffset / (stored[i] - g_lastDepth.depthScale));
    }
    g_readback->UnlockRect();
    return true;
}

void WeaponOverhaul::Blur::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Blur::ReleaseDeviceObjects() {
    ReleaseTargets();
    WeaponOverhaul::Release(g_readback);
    g_owner = nullptr;
    g_refused = false;
    g_focusShader.Release();
    g_downShader.Release();
    g_blurShader.Release();
    g_compositeShader.Release();
}
