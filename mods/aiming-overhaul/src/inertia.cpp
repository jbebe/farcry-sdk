// The arms' slide behind the look's turn. See inertia.h.
#include "inertia.h"

#include "ease.h"
#include "engine/turn_slide.h"
#include "weight.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    using AimingOverhaul::kDegree;
    // The turn's rate counts up to kFastest radians a second, and nought below kSlowest.
    constexpr float kFastest = 185.0f * kDegree;
    constexpr float kSlowest = 1.0f * kDegree;
    // Radians a second below which the followed turn is at rest.
    constexpr float kRest = 0.01f * kDegree;
    // Metres the arms slide for each radian a second: twice the game's sideways slide, which is
    // 1.1 cm at 126 degrees a second.
    constexpr float kSlidePerRate = 2.0f * 0.011f / (126.0f * kDegree);
    // Radians the arms point behind the turn for each radian a second, 4 degrees at 126 degrees a
    // second, and at most kMostLag.
    constexpr float kLagPerRate = 4.0f / 126.0f;
    constexpr float kMostLag = 8.0f * kDegree;
    // Down the sights the gun turns about its own middle instead, its muzzle leading the turn and its
    // stock trailing: radians per radian a second, 1.5 degrees at 126 degrees a second, and at most
    // kMostSightTurn.
    constexpr float kSightTurnPerRate = 1.5f / 126.0f;
    constexpr float kMostSightTurn = 4.0f * kDegree;
    // Seconds the slide takes to follow the turn and come back: kLightBack for a gun of kLight
    // kilograms up to kHeavyBack for one of kHeavy, stretched up to kTwitch times after a turn at
    // kFastest, which is forgotten over kTwitchFade seconds.
    constexpr float kLight = 1.0f;
    constexpr float kHeavy = 7.5f;
    constexpr float kLightBack = 0.2f;
    constexpr float kHeavyBack = 0.6f;
    constexpr float kTwitch = 1.5f;
    constexpr float kTwitchFade = 1.0f;

    std::atomic<bool> g_enabled{true};
    // Whether the game's own slide could be taken over; without it the arms are left to the game.
    bool g_owns = false;
    // The turn as the arms follow it, in radians a second; how hard the last twitch was, nought to
    // one; how much of the slide shows, all of it but down the sights.
    std::atomic<float> g_right{0.0f};
    std::atomic<float> g_up{0.0f};
    float g_twitch = 0.0f;
    float g_free = 1.0f;

    float Rate(float rate) {
        const float clamped = std::clamp(rate, -kFastest, kFastest);
        return std::fabs(clamped) < kSlowest ? 0.0f : clamped;
    }
}

bool AimingOverhaul::Inertia::Install() {
    g_owns = TurnSlide::Install();
    TurnSlide::SetTaken(g_enabled);
    return g_owns;
}

void AimingOverhaul::Inertia::Update(float seconds, float aimed) {
    if (seconds <= 0.0f) {
        return;
    }
    const Aim::Turning turn = Aim::Turned();
    const float right = Rate(turn.right);
    const float up = Rate(turn.up);
    g_twitch = (std::max)(std::hypot(right, up) / kFastest, g_twitch * std::exp(-seconds / kTwitchFade));
    const float heavy = std::clamp((Weight::Kilograms() - kLight) / (kHeavy - kLight), 0.0f, 1.0f);
    const float back = std::lerp(kLightBack, kHeavyBack, heavy) * std::lerp(1.0f, kTwitch, g_twitch);
    // Come to rest exactly, so the pose stops changing and the bones are not posed again.
    const auto settle = [&](float lagged, float rate) {
        const float next = Follow(lagged, rate, seconds, back);
        return rate == 0.0f && std::fabs(next) < kRest ? 0.0f : next;
    };
    g_right = settle(g_right, right);
    g_up = settle(g_up, up);
    g_free = 1.0f - aimed;
}

AimingOverhaul::Body::Offset AimingOverhaul::Inertia::Posed() {
    if (!g_enabled || !g_owns) {
        return {};
    }
    // At the hip, turning right the arms slide and point left; pitching up, they slide and point down.
    const float slide = -kSlidePerRate * g_free;
    const float lag = kLagPerRate * g_free;
    const Body::Offset hip = {SoftLimit(lag * g_right, kMostLag), SoftLimit(-lag * g_up, kMostLag),
                              0.0f, slide * g_right, 0.0f, slide * g_up};
    // Down the sights, turning right the muzzle leads right; pitching up, it leads up.
    const float lead = kSightTurnPerRate * (1.0f - g_free);
    return hip + Body::AboutGun(SoftLimit(-lead * g_right, kMostSightTurn),
                                SoftLimit(lead * g_up, kMostSightTurn));
}

AimingOverhaul::Aim::Turning AimingOverhaul::Inertia::Lagged() {
    return {g_right, g_up};
}

void AimingOverhaul::Inertia::SetEnabled(bool enabled) {
    g_enabled = enabled;
    TurnSlide::SetTaken(enabled);
}
