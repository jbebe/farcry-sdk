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
    constexpr uint32_t kAim = FCSE::Crc32("startironsight");
    constexpr uint32_t kAimReleased = FCSE::Crc32("stopironsight");

    // Metres from the feet to the eye, which the body rolls about.
    constexpr float kEyeHeight = 1.6f;
    // Metres ahead that must be clear for the view to count as open, and the share of a full lean
    // tried in steps, smallest first.
    constexpr float kProbe = 1.2f;
    constexpr int kSteps = 3;
    // Seconds between looks, so a lean holds for at least that long.
    constexpr float kInterval = 0.1f;

    std::atomic<bool> g_enabled{true};
    bool g_aimHeld = false;
    float g_sinceLook = kInterval;
    float g_target = 0.0f;

    bool Open(const float from[3], const float forward[3]) {
        const float probe[3] = {forward[0] * kProbe, forward[1] * kProbe, forward[2] * kProbe};
        return AimingOverhaul::Ray::Cast(from, probe) >= kProbe * 0.99f;
    }

    // Whether the eye can lean by `shift` from `base` and see past what is ahead from there.
    bool Clears(const float base[3], const float shift[3], const float forward[3]) {
        const float length = std::sqrt(shift[0] * shift[0] + shift[1] * shift[1] + shift[2] * shift[2]);
        if (AimingOverhaul::Ray::Cast(base, shift) < length * 0.99f) {
            return false;
        }
        const float leaned[3] = {base[0] + shift[0], base[1] + shift[1], base[2] + shift[2]};
        return Open(leaned, forward);
    }

    float Search(const float base[3], const float forward[3]) {
        // The side leaned to last is tried first, so a lean does not flip between equal sides.
        const float first = g_target < 0.0f ? -1.0f : 1.0f;
        for (int step = 1; step <= kSteps; step++) {
            for (const float side : {first, -first}) {
                const float roll = side * AimingOverhaul::Lean::kMostRoll * step / kSteps;
                float shift[3];
                AimingOverhaul::Peek::Shift(forward, 0.0f, roll, shift);
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
    Shift(forward, roll, 0.0f, back);
    const float base[3] = {eye[0] + back[0], eye[1] + back[1], eye[2] + back[2]};
    g_target = Open(base, forward) ? 0.0f : Search(base, forward);
    return g_target;
}

void AimingOverhaul::Peek::Shift(const float forward[3], float from, float to, float out[3]) {
    const float level = std::hypot(forward[0], forward[1]);
    const float right[2] = {level > 0.0f ? forward[1] / level : 1.0f,
                            level > 0.0f ? -forward[0] / level : 0.0f};
    const float side = kEyeHeight * (std::sin(to) - std::sin(from));
    out[0] = right[0] * side;
    out[1] = right[1] * side;
    out[2] = kEyeHeight * (std::cos(to) - std::cos(from));
}

void AimingOverhaul::Peek::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
