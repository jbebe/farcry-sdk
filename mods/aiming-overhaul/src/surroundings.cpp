// The second view is shrunk to half its size and blurred across and down, as the other eye sees
// past the scope, before it is laid down outside the eyepiece.
#include "surroundings.h"

#include "ease.h"
#include "engine/aim.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/second_view.h"
#include "engine/shader.h"
#include "eyepiece.h"
#include "soft_copy.h"

#include "surroundings_ps.h"

#include <atomic>

namespace {
    // How soft the surroundings are, the blur's sigma as a share of the screen's height.
    constexpr float kSoftness = 0.007f;
    // Seconds the surroundings take to soften once the eyepiece opens, and the seconds it has to
    // be gone for to open anew.
    constexpr float kSoftening = 0.25f;
    constexpr double kGone = 0.1;

    AimingOverhaul::PixelShader g_shader{"surroundings", g_surroundingsPixelShader};
    AimingOverhaul::SoftCopy g_soft{"surroundings"};

    std::atomic<bool> g_enabled{true};
    // The camera's field of view the last time the player was not aiming, in radians.
    float g_ownFov = 0.0f;
    // On the render thread, when the eyepiece opened and when it was last seen open.
    double g_openedAt = 0.0;
    double g_seenOpen = -1e9;

    // The second view, shrunk and blurred across and down; null if it could not be.
    IDirect3DTexture9* Soften(IDirect3DDevice9* device, IDirect3DTexture9* view) {
        IDirect3DSurface9* source = nullptr;
        view->GetSurfaceLevel(0, &source);
        IDirect3DTexture9* soft = g_soft.Soften(device, source, kSoftness);
        AimingOverhaul::Release(source);
        return soft;
    }
}

float AimingOverhaul::Surroundings::Want(float cameraFov) {
    if (!Aim::Aiming()) {
        g_ownFov = cameraFov;
    }
    return g_enabled && Eyepiece::Expected() ? g_ownFov : 0.0f;
}

void AimingOverhaul::Surroundings::BeforeColour(IDirect3DDevice9* device) {
    IDirect3DTexture9* view = SecondView::Latest();
    const std::optional<Eyepiece::Opening> opening = Eyepiece::Open();
    const double now = Now();
    if (opening) {
        if (now - g_seenOpen > kGone) {
            g_openedAt = now;
        }
        g_seenOpen = now;
    }
    IDirect3DPixelShader9* shader =
        view != nullptr && opening && g_enabled ? g_shader.Get(device) : nullptr;
    IDirect3DTexture9* soft = shader != nullptr ? Soften(device, view) : nullptr;
    if (soft == nullptr) {
        return;
    }
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float constants[8] = {
        width / height, opening->x, opening->y, opening->radius,
        Through(now - g_openedAt, kSoftening), 0.0f, 0.0f, 0.0f,
    };

    ScreenDraw draw(device, 0, 2);
    device->SetPixelShaderConstantF(0, constants, 2);
    device->SetTexture(0, soft);
    device->SetTexture(1, view);
    draw.Linear(0);
    draw.Linear(1);
    draw.KeepAlpha();
    device->SetPixelShader(shader);
    draw.Quad(0.0f, 0.0f, width, height);
}

void AimingOverhaul::Surroundings::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void AimingOverhaul::Surroundings::ReleaseDeviceObjects() {
    g_shader.Release();
    g_soft.Release();
}
