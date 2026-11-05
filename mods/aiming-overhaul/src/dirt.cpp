// Dirt in the eye. See dirt.h.
#include "dirt.h"

#include "ease.h"
#include "engine/aim.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "soft_copy.h"
#include "tuning.h"

#include "dirt_ps.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <iterator>
#include <mutex>
#include <random>

namespace {
    // The logic materials, by disId, that throw dirt: concrete, pebbles, hard and soft ground, mud,
    // sand, tile, roof tile and the three woods.
    constexpr uint8_t kDirty[] = {2, 3, 4, 5, 6, 7, 8, 9, 16, 17, 18};

    // How strong dirt from the edge of the range is, against one at the eye.
    constexpr float kWeakest = 0.4f;
    // An impact further behind the eye than this share of the way throws none.
    constexpr float kBehind = -0.2f;
    // Where the dirt lands on the screen, in screen fractions: the middle's height, and how far off
    // it an impact straight to the side or straight above puts it.
    constexpr float kMiddle = 0.55f;
    constexpr float kSideways = 0.3f;
    constexpr float kUpward = 0.25f;
    // Each throw's second, smaller blob: its size against the first's, and how far off it may land
    // at full strength, in screen heights across and down.
    constexpr float kSecondSmallest = 0.45f;
    constexpr float kSecondLargest = 0.85f;
    constexpr float kSpreadAcross = 0.24f;
    constexpr float kSpreadDown = 0.18f;
    // A blob is not quite round: its height over its width, at least and at most.
    constexpr float kFlattest = 0.75f;
    constexpr float kTallest = 1.15f;
    // Seconds a blob takes to appear, and holds whole before it clears, at most.
    constexpr float kAppear = 0.05f;
    constexpr float kHold = 0.6f;
    // Impacts closer together than this, in seconds, throw one lot of dirt, the strongest.
    constexpr double kTogether = 0.15;
    // The screen height the blur is given for.
    constexpr float kBlurLines = 1080.0f;

    // A blob: its centre in screen fractions, y down, its radius in screen heights, its height over
    // its width, how strong its throw was, and when it was thrown.
    struct Blob {
        float x;
        float y;
        float radius;
        float aspect;
        float strength;
        double thrown;
    };
    constexpr size_t kBlobs = 4;
    // DirtPS's constants: the screen, then the blobs, their darkness and their blur.
    constexpr UINT kConstants = 7;

    std::atomic<bool> g_enabled{true};
    std::mutex g_lock;
    std::array<Blob, kBlobs> g_blobs = {};
    size_t g_next = 0;
    std::minstd_rand g_random{20261105};

    AimingOverhaul::PixelShader g_shader{"dirt", g_dirtPixelShader};
    AimingOverhaul::SoftCopy g_soft{"dirt"};

    double Now() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch())
            .count();
    }

    float Between(float least, float most) {
        return std::uniform_real_distribution<float>(least, most)(g_random);
    }

    // How strong dirt from an impact `distance` metres from the eye is, from nought to one.
    float Strength(float distance) {
        const float range = AimingOverhaul::Tuning::DirtInTheEye().range;
        return kWeakest + (1.0f - kWeakest) * std::clamp(1.0f - distance / range, 0.0f, 1.0f);
    }

    // Throws a lot of dirt centred at `x`, `y` in screen fractions, at `strength` from nought to
    // one: a smaller blob near it, then a blob there, which is the throw's last.
    void ThrowAt(float x, float y, float strength) {
        const std::lock_guard<std::mutex> lock(g_lock);
        const double now = Now();
        const Blob& last = g_blobs[(g_next + kBlobs - 1) % kBlobs];
        if (now - last.thrown < kTogether && strength <= last.strength) {
            return;
        }
        const float radius = 0.5f * AimingOverhaul::Tuning::DirtInTheEye().size * strength;
        g_blobs[g_next++ % kBlobs] = {
            x + Between(-0.5f, 0.5f) * kSpreadAcross * strength,
            y + Between(-0.5f, 0.5f) * kSpreadDown * strength,
            radius * Between(kSecondSmallest, kSecondLargest),
            Between(kFlattest, kTallest),
            strength,
            now,
        };
        g_blobs[g_next++ % kBlobs] = {x, y, radius, Between(kFlattest, kTallest), strength, now};
    }

    // How much of a blob is left `age` seconds after it was thrown, from nought to one.
    float Left(double age, float clear) {
        const float hold = (std::min)(kHold, 0.5f * clear);
        const float seconds = static_cast<float>(age);
        const float appeared = AimingOverhaul::Ease(std::clamp(seconds / kAppear, 0.0f, 1.0f));
        const float cleared =
            AimingOverhaul::Ease(std::clamp((seconds - hold) / (clear - hold), 0.0f, 1.0f));
        return appeared * (1.0f - cleared);
    }
}

void AimingOverhaul::Dirt::OnImpact(const float point[3], uint8_t material) {
    const float* eye = Aim::Eye();
    if (!g_enabled || eye == nullptr || std::find(std::begin(kDirty), std::end(kDirty), material) ==
                                            std::end(kDirty)) {
        return;
    }
    const float away[3] = {point[0] - eye[0], point[1] - eye[1], point[2] - eye[2]};
    const float distance = std::hypot(away[0], away[1], away[2]);
    if (distance >= Tuning::DirtInTheEye().range || distance <= 0.0f) {
        return;
    }
    // The view's axes: ahead, right along the level, and up.
    float ahead[3];
    Aim::Forward(ahead);
    const float level = std::hypot(ahead[0], ahead[1]);
    if (level <= 0.0f) {
        return;
    }
    const float right[3] = {ahead[1] / level, -ahead[0] / level, 0.0f};
    const float up[3] = {right[1] * ahead[2], -right[0] * ahead[2],
                         right[0] * ahead[1] - right[1] * ahead[0]};
    const auto share = [&](const float (&axis)[3]) {
        return (away[0] * axis[0] + away[1] * axis[1] + away[2] * axis[2]) / distance;
    };
    if (share(ahead) < kBehind) {
        return;
    }
    ThrowAt(0.5f + kSideways * share(right), kMiddle - kUpward * share(up), Strength(distance));
}

void AimingOverhaul::Dirt::Throw() {
    ThrowAt(0.5f + kSideways, kMiddle, Strength(1.0f));
}

void AimingOverhaul::Dirt::OnComposite(IDirect3DDevice9* device) {
    if (!g_enabled) {
        return;
    }
    const Tuning::DirtLook& look = Tuning::DirtInTheEye();
    // c1 to c4: each blob; c5 and c6: each one's darkness and its blur's share.
    float constants[kConstants * 4] = {};
    bool any = false;
    {
        const std::lock_guard<std::mutex> lock(g_lock);
        const double now = Now();
        for (size_t i = 0; i < kBlobs; ++i) {
            const Blob& blob = g_blobs[i];
            const float left = blob.thrown > 0.0 ? Left(now - blob.thrown, look.clear) : 0.0f;
            const float placed[4] = {blob.x, blob.y, blob.radius, blob.aspect};
            std::copy_n(placed, 4, constants + 4 + 4 * i);
            constants[20 + i] = look.darkness * blob.strength * left;
            constants[24 + i] = blob.strength * left;
            any |= left > 0.0f;
        }
    }
    IDirect3DPixelShader9* shader = any ? g_shader.Get(device) : nullptr;
    IDirect3DSurface9* frame = nullptr;
    if (shader == nullptr || FAILED(device->GetRenderTarget(0, &frame)) || frame == nullptr) {
        return;
    }
    D3DSURFACE_DESC desc = {};
    frame->GetDesc(&desc);
    IDirect3DTexture9* soft = g_soft.Soften(device, frame, look.blur / kBlurLines);
    Release(frame);
    if (soft == nullptr) {
        return;
    }
    const float width = static_cast<float>(desc.Width);
    const float height = static_cast<float>(desc.Height);
    constants[0] = width / height;

    // Laid on premultiplied: the blurred view by its share and the darkness over both.
    ScreenDraw draw(device, 0, kConstants);
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    draw.KeepAlpha();
    device->SetPixelShaderConstantF(0, constants, kConstants);
    device->SetTexture(0, soft);
    draw.Linear(0);
    device->SetPixelShader(shader);
    draw.Quad(0.0f, 0.0f, width, height);
}

void AimingOverhaul::Dirt::ReleaseDeviceObjects() {
    g_shader.Release();
    g_soft.Release();
}

void AimingOverhaul::Dirt::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
