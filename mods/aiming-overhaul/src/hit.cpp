// A hit on the player. See hit.h.
#include "hit.h"

#include "ease.h"
#include "engine/aim.h"
#include "engine/frame.h"
#include "engine/plain_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "tuning.h"

#include "hit_ps.h"

#include <algorithm>
#include <atomic>
#include <mutex>

namespace {
    using AimingOverhaul::Now;
    using AimingOverhaul::Through;

    // A hit's strength runs from kWeakest for a graze up to one for a hit taking kHardest of the
    // player's health or more.
    constexpr float kWeakest = 0.4f;
    constexpr float kHardest = 0.5f;
    // Hits closer together than this, in seconds, are one, their health lost added up: a shotgun's
    // pellets.
    constexpr double kTogether = 0.15;
    // A hit not yet seen on foot this many seconds after it, as from a vehicle, never punches.
    constexpr double kStale = 0.25;
    // The vignette darkens over kDarken seconds and starts to clear kDarkHold after the hit.
    constexpr float kDarken = 0.12f;
    constexpr float kDarkHold = 0.5f;
    // The second image is kDouble of the view from a head hit on, and lies kDoubleDown of its
    // distance across below the first.
    constexpr float kDouble = 0.5f;
    constexpr float kDoubleDown = 0.35f;
    // The screen height the double image's distance is given for.
    constexpr float kApartLines = 1080.0f;
    constexpr double kNever = -1e9;

    // The last hit off the head, when and how much health it took, and when the head was last hit.
    struct Hits {
        double at = kNever;
        float lost = 0.0f;
        double headAt = kNever;
    };

    std::atomic<bool> g_enabled{true};
    std::mutex g_lock;
    Hits g_hits;

    // On the game thread: the punch, and the hit it last struck for and how hard.
    AimingOverhaul::Spring g_punch;
    double g_struckAt = kNever;
    float g_struckPeak = 0.0f;

    // HitPS's constants: the double image, then the vignette.
    constexpr UINT kConstants = 2;
    AimingOverhaul::PixelShader g_shader{"hit", g_hitPixelShader};

    Hits Latest() {
        const std::lock_guard<std::mutex> lock(g_lock);
        return g_hits;
    }

    float Strength(float lost) {
        return kWeakest + (1.0f - kWeakest) * (std::min)(lost / kHardest, 1.0f);
    }

    // How far a hit `since` seconds ago has darkened the edges, nought to one.
    float Darkened(double since, const AimingOverhaul::Tuning::HitLook& look) {
        return Through(since, kDarken) *
               (1.0f - Through(since - kDarkHold, look.vignetteGone - kDarkHold));
    }

    void Take(AimingOverhaul::Hit::Where where, float lost) {
        if (!g_enabled) {
            return;
        }
        const std::lock_guard<std::mutex> lock(g_lock);
        const double now = Now();
        if (where == AimingOverhaul::Hit::Where::kHead) {
            if (now - g_hits.headAt >= kTogether) {
                g_hits.headAt = now;
            }
            return;
        }
        if (now - g_hits.at < kTogether) {
            g_hits.lost += lost;
        } else {
            g_hits.at = now;
            g_hits.lost = lost;
        }
    }
}

void AimingOverhaul::Hit::OnHit(bool head, float lost) {
    Take(head ? Where::kHead : Where::kBody, lost);
}

void AimingOverhaul::Hit::Try(Where where) {
    Take(where, kHardest);
}

AimingOverhaul::Hit::Punch AimingOverhaul::Hit::Update(float seconds) {
    const Hits hits = Latest();
    const Tuning::HitLook& look = Tuning::BeingHit();
    const double now = Now();
    const float peak = Strength(hits.lost);
    const float omega = 1.0f / look.peak;
    if (hits.at != g_struckAt) {
        g_struckAt = hits.at;
        g_struckPeak = now - hits.at < kStale ? 0.0f : peak;
    }
    // Pellets after the first punch only as much harder as they make it.
    if (hits.at != kNever && peak > g_struckPeak) {
        g_punch.Strike(peak - g_struckPeak, omega);
        g_struckPeak = peak;
    }
    g_punch.Settle(omega, seconds);
    const float punch = (1.0f - Aim::Scoped()) * g_punch.x;
    return {punch * look.back, -punch * look.down * kDegree};
}

void AimingOverhaul::Hit::OnComposite(IDirect3DDevice9* device, IDirect3DSurface9* frame) {
    if (!g_enabled) {
        return;
    }
    const Hits hits = Latest();
    const Tuning::HitLook& look = Tuning::BeingHit();
    const double now = Now();
    const double sinceHead = now - hits.headAt;
    // A hit on the head darkens the edges as the hardest body hit does.
    const float vignette =
        look.vignette *
        (std::max)(Strength(hits.lost) * Darkened(now - hits.at, look), Darkened(sinceHead, look));
    const float seenDouble = kDouble * (1.0f - Through(sinceHead, look.doubleGone));
    IDirect3DPixelShader9* shader =
        vignette > 0.0f || seenDouble > 0.0f ? g_shader.Get(device) : nullptr;
    if (shader == nullptr) {
        return;
    }
    IDirect3DTexture9* copy = seenDouble > 0.0f ? FrameCopy().Take(device, frame) : nullptr;
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float apart = look.apart * height / kApartLines;
    const float constants[kConstants * 4] = {
        apart / width, kDoubleDown * apart / height, copy != nullptr ? seenDouble : 0.0f, 0.0f,
        vignette,      1.0f - look.reach,           0.0f,                                 0.0f,
    };

    // The second image by its share, and the dark over both.
    ScreenDraw draw(device, 0, kConstants);
    draw.Premultiplied();
    device->SetPixelShaderConstantF(0, constants, kConstants);
    device->SetTexture(0, copy);
    draw.Linear(0);
    device->SetPixelShader(shader);
    draw.Quad(0.0f, 0.0f, width, height);
}

void AimingOverhaul::Hit::ReleaseDeviceObjects() {
    g_shader.Release();
}

void AimingOverhaul::Hit::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
