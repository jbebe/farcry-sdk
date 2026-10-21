// Auto-lean: aiming with a wall close ahead leans out to the first side that is clear past it, just
// far enough to see.
#include "peek.h"

#include "engine/ray.h"
#include "lean.h"

#include <atomic>
#include <cmath>
#include <initializer_list>

namespace {
    // Metres ahead that must be clear for the view to count as open, and the steps a full lean is
    // tried in, smallest first.
    constexpr float kProbe = 1.2f;
    constexpr int kSteps = 3;
    // Seconds between looks, so a lean holds for at least that long.
    constexpr float kInterval = 0.1f;

    std::atomic<bool> g_enabled{true};
    float g_sinceLook = kInterval;
    float g_target = 0.0f;
    float g_heading = 0.0f;

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

    bool Clears(const float base[3], const float forward[3], float roll) {
        float shift[3];
        AimingOverhaul::Lean::Shift(forward, 0.0f, roll, shift);
        return Clears(base, shift, forward);
    }

    // The lean already taken while it still clears, so a lean holds rather than hunting between
    // steps; else the least that does, the side leaned to last tried first.
    float Search(const float base[3], const float forward[3]) {
        if (g_target != 0.0f && Clears(base, forward, g_target)) {
            return g_target;
        }
        const float first = g_target < 0.0f ? -1.0f : 1.0f;
        for (int step = 1; step <= kSteps; step++) {
            for (const float side : {first, -first}) {
                const float roll = side * AimingOverhaul::Lean::kMostRoll * step / kSteps;
                if (Clears(base, forward, roll)) {
                    return roll;
                }
            }
        }
        return 0.0f;
    }
}

float AimingOverhaul::Peek::Update(float seconds, bool aimHeld, const float eye[3],
                                   const float forward[3], float roll) {
    g_sinceLook += seconds;
    if (!g_enabled || !aimHeld || eye == nullptr) {
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
    const float target = Ray::Clear(base, forward, kProbe) ? 0.0f : Search(base, forward);
    if (target != 0.0f && (g_target == 0.0f || (target < 0.0f) != (g_target < 0.0f))) {
        g_heading = std::atan2(-forward[0], forward[1]);
    }
    g_target = target;
    return g_target;
}

float AimingOverhaul::Peek::Heading() {
    return g_heading;
}

void AimingOverhaul::Peek::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
