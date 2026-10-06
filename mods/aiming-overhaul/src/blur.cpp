// The gun out of focus down the iron sights, the eye focused on the front sight. The focus is found
// on the front sight, and the gun blurred by how far each part is from it, past its own edge too.
#include "blur.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "blur_blur_ps.h"
#include "blur_composite_ps.h"
#include "blur_down_ps.h"
#include "blur_focus_ps.h"

#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>

namespace {
    // The eye's pupil, in metres, which is how fast the blur grows away from the front sight.
    constexpr float kPupil = 0.008f;
    // The largest blur radius, as a share of the screen's height, and the small one, as a share of
    // the largest.
    constexpr float kLargest = 0.012f;
    constexpr float kSmall = 1.0f / 3.0f;
    // BlurPS's steps either side.
    constexpr float kSteps = 6.0f;
    // Seconds for the eye to refocus most of the way out to the front sight, and in from it.
    constexpr float kRefocusOut = 0.1f;
    constexpr float kRefocusIn = 1.5f;

    constexpr UINT kLens = 0;
    constexpr UINT kConstantCount = 3;

    constexpr D3DFORMAT kHalfFormat = D3DFMT_A16B16G16R16F;
    constexpr D3DFORMAT kFocusFormat = D3DFMT_R32F;

    AimingOverhaul::PixelShader g_focusShader{"blur focus", g_blurFocusPixelShader};
    AimingOverhaul::PixelShader g_downShader{"blur down", g_blurDownPixelShader};
    AimingOverhaul::PixelShader g_blurShader{"blur", g_blurPixelShader};
    AimingOverhaul::PixelShader g_compositeShader{"blur composite", g_blurCompositePixelShader};

    std::atomic<bool> g_enabled{true};
    IDirect3DDevice9* g_owner = nullptr;
    // The scene resolved; the focus, this frame's and the last; and at half size the gun taken
    // down, a scratch target, and the gun blurred a little and a lot.
    AimingOverhaul::Target g_copy;
    AimingOverhaul::Target g_focus[2];
    AimingOverhaul::Target g_halves[4];
    D3DSURFACE_DESC g_copyDesc = {};
    UINT g_halfWidth = 0;
    UINT g_halfHeight = 0;
    // Which focus target this frame writes, whether both still hold whatever they were made with,
    // and the count of weapon changes the focus belongs to.
    int g_focusIndex = 0;
    bool g_focusFresh = true;
    uint32_t g_focusWeapon = 0;
    std::chrono::steady_clock::time_point g_lastDraw;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;

    void ReleaseTargets() {
        AimingOverhaul::Release(g_copy);
        for (AimingOverhaul::Target& focus : g_focus) {
            AimingOverhaul::Release(focus);
        }
        for (AimingOverhaul::Target& half : g_halves) {
            AimingOverhaul::Release(half);
        }
        g_focusFresh = true;
    }

    bool Create(IDirect3DDevice9* device, UINT width, UINT height, D3DFORMAT format,
                AimingOverhaul::Target& target) {
        const HRESULT created = AimingOverhaul::CreateTarget(device, width, height, format, target);
        if (FAILED(created)) {
            ReleaseTargets();
            g_refused = true;
            FCSE::Logf("blur: the device refused a %ux%u target of format %d, 0x%08lX", width,
                       height, format, static_cast<unsigned long>(created));
            return false;
        }
        return true;
    }

    bool EnsureTargets(IDirect3DDevice9* device, const D3DSURFACE_DESC& scene, UINT halfWidth,
                       UINT halfHeight) {
        if (g_owner == device && g_copy.texture != nullptr && g_copyDesc.Width == scene.Width &&
            g_copyDesc.Height == scene.Height && g_copyDesc.Format == scene.Format &&
            g_halfWidth == halfWidth && g_halfHeight == halfHeight) {
            return true;
        }
        ReleaseTargets();
        if (g_refused) {
            return false;
        }
        g_owner = device;
        g_copyDesc = scene;
        g_halfWidth = halfWidth;
        g_halfHeight = halfHeight;
        bool made = Create(device, scene.Width, scene.Height, scene.Format, g_copy);
        for (AimingOverhaul::Target& focus : g_focus) {
            made = made && Create(device, 1, 1, kFocusFormat, focus);
        }
        for (AimingOverhaul::Target& half : g_halves) {
            made = made && Create(device, halfWidth, halfHeight, kHalfFormat, half);
        }
        return made;
    }

    float SecondsSinceLastDraw() {
        const auto now = std::chrono::steady_clock::now();
        const float seconds = std::chrono::duration<float>(now - g_lastDraw).count();
        g_lastDraw = now;
        return seconds;
    }

    // One Gaussian over the half-size gun, across and then down, `step` half-size pixels apart.
    void BlurInto(AimingOverhaul::ScreenDraw& draw, const AimingOverhaul::Target& into,
                  float step) {
        const float width = static_cast<float>(g_halfWidth);
        const float height = static_cast<float>(g_halfHeight);
        const float across[4] = {step / width, 0.0f, 0.0f, 0.0f};
        const float downward[4] = {0.0f, step / height, 0.0f, 0.0f};
        draw.Separable(AimingOverhaul::Blur::kStep, across, downward, g_halves[0].texture,
                       g_halves[1], into.surface, width, height);
    }

    void Draw(const AimingOverhaul::Frame::Pass& pass,
              const AimingOverhaul::WeaponDraws::Depth& depth, float settled) {
        IDirect3DDevice9* device = pass.device;
        D3DSURFACE_DESC scene = {};
        pass.target->GetDesc(&scene);
        IDirect3DPixelShader9* focus = g_focusShader.Get(device);
        IDirect3DPixelShader9* down = g_downShader.Get(device);
        IDirect3DPixelShader9* blur = g_blurShader.Get(device);
        IDirect3DPixelShader9* composite = g_compositeShader.Get(device);
        if (focus == nullptr || down == nullptr || blur == nullptr || composite == nullptr ||
            !EnsureTargets(device, scene, depth.width, depth.height) ||
            FAILED(device->StretchRect(pass.target, nullptr, g_copy.surface, nullptr,
                                       D3DTEXF_NONE))) {
            return;
        }

        const float halfWidth = static_cast<float>(depth.width);
        const float halfHeight = static_cast<float>(depth.height);
        // The largest blur in kSteps steps, in half-size pixels.
        const float step = kLargest * halfHeight / kSteps;
        // The blur's diameter is the pupil times the dioptres from focus; dioptres are stored depth
        // over the depth offset, and an angle covers the vertical scale over two of the height.
        const float lens =
            kPupil * depth.projection.verticalScale /
            (4.0f * std::abs(depth.projection.depthOffset) * kLargest);
        // How far the focus moves toward this frame's when that is farther, and when it is nearer.
        const float seconds = SecondsSinceLastDraw();
        const float refocusOut = 1.0f - std::exp(-seconds / kRefocusOut);
        const float refocusIn = 1.0f - std::exp(-seconds / kRefocusIn);
        const float constants[kConstantCount * 4] = {
            lens, settled, refocusOut, refocusIn,
            0.0f, 0.0f, 0.0f, 0.0f,
            1.0f / halfWidth, 1.0f / halfHeight, 0.0f, 0.0f,
        };
        const AimingOverhaul::Target& focusNow = g_focus[g_focusIndex];
        const AimingOverhaul::Target& focusBefore = g_focus[g_focusIndex ^ 1];

        AimingOverhaul::ScreenDraw draw(device, kLens, kConstantCount);
        device->SetPixelShaderConstantF(kLens, constants, kConstantCount);
        for (DWORD sampler : {0u, 1u, 2u, 4u}) {
            draw.Linear(sampler);
        }
        // A half-size target cannot share the scene's multisampled depth.
        device->SetDepthStencilSurface(nullptr);

        if (g_focusFresh) {
            for (const AimingOverhaul::Target& target : g_focus) {
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
        BlurInto(draw, g_halves[2], step * kSmall);
        BlurInto(draw, g_halves[3], step);

        device->SetRenderTarget(0, pass.target);
        draw.KeepAlpha();
        device->SetTexture(0, g_copy.texture);
        device->SetTexture(1, g_halves[2].texture);
        device->SetTexture(2, g_halves[3].texture);
        device->SetTexture(4, g_halves[0].texture);
        device->SetPixelShader(composite);
        draw.Quad(0.0f, 0.0f, static_cast<float>(scene.Width), static_cast<float>(scene.Height));

        g_focusIndex ^= 1;
    }
}

void AimingOverhaul::Blur::OnGunPass(const Frame::Pass& pass, const WeaponDraws::Depth& depth) {
    const float settled = g_enabled ? Aim::Settled() : 0.0f;
    if (settled <= 0.0f) {
        return;
    }
    // Another gun's front sight is somewhere else, so its focus is found afresh.
    const uint32_t changes = Aim::WeaponChanges();
    if (changes != g_focusWeapon) {
        g_focusWeapon = changes;
        g_focusFresh = true;
    }
    Draw(pass, depth, settled);
}

void AimingOverhaul::Blur::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void AimingOverhaul::Blur::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
    g_focusShader.Release();
    g_downShader.Release();
    g_blurShader.Release();
    g_compositeShader.Release();
}
