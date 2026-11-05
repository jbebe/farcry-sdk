// The arms' slide behind the look's pitch. See pitch_slide.h.
#include "pitch_slide.h"

#include "ease.h"
#include "engine/aim.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    // As the game takes the turn's rate for its sideways slide: no faster than kFastest radians a
    // second, eased a tenth of the way each frame at 60 a second, which is kEasing seconds, and
    // nought below kSlowest.
    using AimingOverhaul::kDegree;
    constexpr float kFastest = 185.0f * kDegree;
    constexpr float kEasing = 0.158f;
    constexpr float kSlowest = 1.0f * kDegree;
    // Metres the arms slide for each radian a second, which the game's sideways slide is measured
    // at: 1.1 cm at 126 degrees a second.
    constexpr float kSlidePerRate = 0.011f / (126.0f * kDegree);

    std::atomic<bool> g_enabled{true};
    // How fast the look pitches up, eased, in radians a second; how much of the slide shows, all of
    // it but down the sights.
    float g_rate = 0.0f;
    float g_free = 1.0f;
}

void AimingOverhaul::PitchSlide::Update(float seconds, float aimed) {
    if (seconds <= 0.0f) {
        return;
    }
    const float rate = std::clamp(Aim::Turned().up, -kFastest, kFastest);
    g_rate = Follow(g_rate, rate, seconds, kEasing);
    if (std::fabs(g_rate) < kSlowest) {
        g_rate = 0.0f;
    }
    g_free = 1.0f - aimed;
}

AimingOverhaul::Body::Offset AimingOverhaul::PitchSlide::Posed() {
    if (!g_enabled) {
        return {};
    }
    // Pitching up, the arms slide down.
    return {0.0f, 0.0f, 0.0f, 0.0f, 0.0f, -kSlidePerRate * g_rate * g_free};
}

void AimingOverhaul::PitchSlide::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
