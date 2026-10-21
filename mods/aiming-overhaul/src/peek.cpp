// Auto-lean: aiming with a wall close ahead leans out to the first side that is clear past it, just
// far enough to see.
#include "peek.h"

#include "engine/ray.h"
#include "fcse_api.h"
#include "lean.h"

#include <atomic>
#include <cmath>
#include <initializer_list>

namespace {
    // The aim key itself, since the sights are let go while the gun is swung down at a wall.
    constexpr uint32_t kAim = FCSE::Crc32("startironsight");
    constexpr uint32_t kAimReleased = FCSE::Crc32("stopironsight");

    // Metres ahead that must be clear for the view to count as open, and the steps a full lean is
    // tried in, smallest first.
    constexpr float kProbe = 1.2f;
    constexpr int kSteps = 3;
    // Seconds between looks, so a lean holds for at least that long.
    constexpr float kInterval = 0.1f;

    std::atomic<bool> g_enabled{true};
    bool g_aimHeld = false;
    float g_sinceLook = kInterval;
    float g_target = 0.0f;

    // Whether the eye can lean by `shift` from `base` and see past what is ahead from there.
    bool Clears(const float base[3], const float shift[3], const float forward[3]) {
        const float length = std::hypot(shift[0], shift[1], shift[2]);
        const float way[3] = {shift[0] / length, shift[1] / length, shift[2] / length};
        if (!AimingOverhaul::Ray::Clear(base, way, length)) {
            return false;
        }
        const float leaned[3] = {base[0] + shift[0], base[1] + shift[1], base[2] + shift[2]};
        return AimingOverhaul::Ray::Clear(leaned, forward, kProbe);
    }

    float Search(const float base[3], const float forward[3]) {
        // The side leaned to last is tried first, so a lean does not flip between equal sides.
        const float first = g_target < 0.0f ? -1.0f : 1.0f;
        for (int step = 1; step <= kSteps; step++) {
            for (const float side : {first, -first}) {
                const float roll = side * AimingOverhaul::Lean::kMostRoll * step / kSteps;
                float shift[3];
                AimingOverhaul::Lean::Shift(forward, 0.0f, roll, shift);
                if (Clears(base, shift, forward)) {
                    return roll;
                }
            }
        }
        return 0.0f;
    }
}

void AimingOverhaul::Peek::OnSignal(uint32_t signal) {
    if (signal == kAim) {
        g_aimHeld = true;
    } else if (signal == kAimReleased) {
        g_aimHeld = false;
    }
}

float AimingOverhaul::Peek::Update(float seconds, const float eye[3], const float forward[3],
                                   float roll) {
    g_sinceLook += seconds;
    if (!g_enabled || !g_aimHeld || eye == nullptr) {
        g_target = 0.0f;
        return g_target;
    }
    if (g_sinceLook < kInterval) {
        return g_target;
    }
    g_sinceLook = 0.0f;
    float back[3];
    Lean::Shift(forward, roll, 0.0f, back);
    const float base[3] = {eye[0] + back[0], eye[1] + back[1], eye[2] + back[2]};
    g_target = Ray::Clear(base, forward, kProbe) ? 0.0f : Search(base, forward);
    return g_target;
}

void AimingOverhaul::Peek::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
