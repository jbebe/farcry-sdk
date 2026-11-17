// The player's health in the picture. See health.h.
#include "health.h"

#include "ease.h"
#include "engine/frame.h"
#include "engine/plain_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "soft_copy.h"
#include "tuning.h"

#include "health_ps.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    // The bars the health is drawn in.
    constexpr float kBars = 5.0f;
    // The glow's blur, its sigma as a share of the screen's height.
    constexpr float kGlowSoftness = 0.02f;
    // Grain is laid in cells a share of the screen's height across, 3 pixels at 1080p, re-rolled each
    // frame.
    constexpr float kGrainCell = 3.0f / 1080.0f;
    constexpr UINT kConstants = 2;

    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_health{1.0f};
    std::atomic<int> g_shown{0};

    // On the render thread: the level shown, nought to three, and when it was last eased.
    float g_level = 0.0f;
    double g_easedAt = 0.0;
    float g_seed = 0.0f;

    AimingOverhaul::PixelShader g_shader{"health", g_healthPixelShader};
    AimingOverhaul::SoftCopy g_soft{"health"};

    // The level a health share calls for: none with 3 bars full, 1 into the 3rd bar, 2 into the 2nd
    // and 3 into the last, so a bar refilled to its top counts as the one above.
    float LevelOf(float health) {
        const float full = std::floor(std::clamp(health, 0.0f, 1.0f) * kBars + 1e-3f);
        return std::clamp(3.0f - full, 0.0f, 3.0f);
    }

    // The look at `level`, between the levels' own: colour left, grain and glow.
    struct Look {
        float colour;
        float grain;
        float glow;
    };

    Look LookAt(float level) {
        const AimingOverhaul::Tuning::HealthLook& look = AimingOverhaul::Tuning::HealthPicture();
        const Look levels[] = {{1.0f, 0.0f, 0.0f},
                               {look.colour3, 0.0f, 0.0f},
                               {look.colour2, look.grain2, look.glow2},
                               {look.colour1, look.grain1, look.glow1}};
        const int below = static_cast<int>(std::floor(level));
        const int above = (std::min)(below + 1, 3);
        const float t = level - static_cast<float>(below);
        const Look& a = levels[below];
        const Look& b = levels[above];
        return {std::lerp(a.colour, b.colour, t), std::lerp(a.grain, b.grain, t),
                std::lerp(a.glow, b.glow, t)};
    }
}

void AimingOverhaul::Health::Set(float health) {
    g_health = health;
}

void AimingOverhaul::Health::OnComposite(IDirect3DDevice9* device, IDirect3DSurface9* frame) {
    const double now = Now();
    const float seconds = static_cast<float>(std::clamp(now - g_easedAt, 0.0, 0.25));
    g_easedAt = now;
    const int shown = g_shown;
    const float target = !g_enabled ? 0.0f : shown > 0 ? static_cast<float>(shown) : LevelOf(g_health);
    g_level = Approach(g_level, target, seconds / (std::max)(Tuning::HealthPicture().fade, 0.01f));
    if (g_level <= 0.0f) {
        return;
    }
    const Look look = LookAt(g_level);
    IDirect3DPixelShader9* shader = g_shader.Get(device);
    IDirect3DTexture9* soft =
        shader != nullptr && look.glow > 0.0f ? g_soft.Soften(device, frame, kGlowSoftness) : nullptr;
    IDirect3DTexture9* copy = shader != nullptr ? FrameCopy().Take(device, frame) : nullptr;
    if (copy == nullptr) {
        return;
    }
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    g_seed = std::fmod(g_seed + 17.31f, 97.0f);
    const float cell = kGrainCell * height;
    const float constants[kConstants * 4] = {
        look.colour, look.grain, soft != nullptr ? look.glow : 0.0f, g_seed,
        width / cell, height / cell, 0.0f, 0.0f,
    };

    ScreenDraw draw(device, 0, kConstants);
    device->SetPixelShaderConstantF(0, constants, kConstants);
    device->SetTexture(0, copy);
    device->SetTexture(1, soft != nullptr ? soft : copy);
    draw.Linear(1);
    draw.KeepAlpha();
    device->SetPixelShader(shader);
    draw.Quad(0.0f, 0.0f, width, height);
}

void AimingOverhaul::Health::Show(int level) {
    g_shown = std::clamp(level, 0, 3);
}

int AimingOverhaul::Health::Shown() {
    return g_shown;
}

void AimingOverhaul::Health::ReleaseDeviceObjects() {
    g_shader.Release();
    g_soft.Release();
}

void AimingOverhaul::Health::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
