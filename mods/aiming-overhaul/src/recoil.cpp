// Recoil that stays: the game's own kick for each shot, its size per gun, wear and sights, turned
// into the look, which carries the view, the gun and the shot. A gun whose archetype has
// `AimingOverhaul.RecoilReturns` (written by data\fragments.py) keeps the game's recoil.
#include "recoil.h"

#include "ease.h"
#include "engine/look.h"
#include "engine/memory.h"
#include "fcse_api.h"
#include "stance.h"
#include "tuning.h"

#include <algorithm>
#include <cmath>
#include <numbers>
#include <random>

namespace {
    // Shots less than kBurstGap seconds apart are one burst. Its first kLightKicks kicks are
    // kLightShare of the gun's, and each shot past its first kSteadyShots spreads by kSpreadShare of
    // the gun's kick more, up to kMostSpreadShots shots' worth.
    constexpr float kBurstGap = 0.25f;
    constexpr int kLightKicks = 2;
    constexpr float kLightShare = 0.25f;
    constexpr int kSteadyShots = 3;
    constexpr float kSpreadShare = 0.25f;
    constexpr int kMostSpreadShots = 8;
    // Past its first kSteadyShots a burst no longer climbs: the arms point the gun off at random
    // instead, growing to their most by its kMostShakeShots-th shot. Each round sends them on
    // kSnapDelay seconds after it, and they ease there over kSnap of the time between rounds; once
    // the burst ends they come back onto the aim over kBackOnAim seconds, and at kRest radians off
    // it they are on it.
    constexpr int kMostShakeShots = 8;
    constexpr float kSnapDelay = 0.05f;
    constexpr float kSnap = 0.12f;
    constexpr float kBackOnAim = 0.15f;
    constexpr float kRest = 1e-5f;

    // A kick rises over the seconds the game gives it, nearly all of it in that time.
    constexpr float kRiseLags = 3.0f;
    constexpr float kShortestRise = 0.01f;

    bool g_enabled = true;
    bool g_returns = false;

    // The shots in the burst so far, and the seconds since its last.
    int g_burst = 0;
    float g_sinceShot = kBurstGap;

    // The gun's last whole kick up, in radians; the kick still to come and how long it takes.
    float g_kickUp = 0.0f;
    AimingOverhaul::Look::Angles g_coming = {};
    float g_rise = kShortestRise;
    // The burst's shake: how far the arms point the gun off the aim, where they head, where the last
    // round sends them and the seconds until it does, and the seconds between the last two rounds.
    AimingOverhaul::Look::Angles g_shaken = {};
    AimingOverhaul::Look::Angles g_heading = {};
    AimingOverhaul::Look::Angles g_next = {};
    float g_untilSnap = 0.0f;
    float g_interval = kBurstGap;

    std::minstd_rand g_random{20261102};

    bool Stays() {
        return g_enabled && !g_returns;
    }

    int Burst() {
        return g_sinceShot > kBurstGap ? 0 : g_burst;
    }

    // Sends the arms somewhere new in a disc, after kSnapDelay, for a round `past` rounds beyond the
    // burst's steady ones, the disc growing to its most by kMostShakeShots.
    void Head(int past) {
        const float grown = (std::min)(
            static_cast<float>(past) / static_cast<float>(kMostShakeShots - kSteadyShots), 1.0f);
        // In full standing on the move; by the sway's own rule half that standing still and a
        // quarter crouched still.
        const float stance = AimingOverhaul::Stance::Sway(0.5f, 0.5f);
        const float reach = AimingOverhaul::Tuning::InHand().shake * std::fabs(g_kickUp) * grown *
                            stance *
                            std::sqrt(std::uniform_real_distribution<float>(0.0f, 1.0f)(g_random));
        const float angle =
            std::uniform_real_distribution<float>(0.0f, 2.0f * std::numbers::pi_v<float>)(g_random);
        g_next = {reach * std::sin(angle), reach * std::cos(angle)};
        g_untilSnap = kSnapDelay;
    }

    // Moves the arms toward where the last round sent them, at the burst's own pace; once the burst
    // ends they come back onto the aim.
    void Shake(float seconds) {
        if (g_untilSnap > 0.0f) {
            g_untilSnap -= seconds;
            if (g_untilSnap <= 0.0f) {
                g_heading = g_next;
            }
        }
        const bool shaking = Stays() && Burst() > kSteadyShots;
        const AimingOverhaul::Look::Angles target = shaking ? g_heading : AimingOverhaul::Look::Angles{};
        const float pace = shaking ? kSnap * g_interval : kBackOnAim;
        // Come to rest exactly, so the pose stops changing and the bones are not posed again.
        const auto follow = [&](float shaken, float to) {
            const float next = AimingOverhaul::Follow(shaken, to, seconds, pace);
            return to == 0.0f && std::fabs(next) < kRest ? 0.0f : next;
        };
        g_shaken = {follow(g_shaken.up, target.up), follow(g_shaken.right, target.right)};
    }

    // `direction`, a unit vector in the world, turned right and up by `by` radians.
    void TurnBy(float direction[3], AimingOverhaul::Look::Angles by) {
        const float level = std::hypot(direction[0], direction[1]);
        if (level <= 0.0f) {
            return;
        }
        const float heading = std::atan2(direction[1], direction[0]) - by.right;
        const float pitch = std::atan2(direction[2], level) + by.up;
        direction[0] = std::cos(pitch) * std::cos(heading);
        direction[1] = std::cos(pitch) * std::sin(heading);
        direction[2] = std::sin(pitch);
    }
}

void AimingOverhaul::Recoil::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void AimingOverhaul::Recoil::FollowWeapon(uint8_t* weapon) {
    g_returns = EntityFlag(weapon, "AimingOverhaul.RecoilReturns");
}

bool AimingOverhaul::Recoil::Take(const Kick::Kick& kick) {
    if (!Stays()) {
        return false;
    }
    const int burst = Burst();
    const float share = burst < kLightKicks ? kLightShare : burst < kSteadyShots ? 1.0f : 0.0f;
    g_interval = burst > 0 ? (std::max)(g_sinceShot, 0.02f) : kBurstGap;
    g_burst = burst + 1;
    g_sinceShot = 0.0f;
    g_kickUp = kick.up;
    if (g_burst > kSteadyShots) {
        Head(g_burst - kSteadyShots);
    }
    g_coming.up += kick.up * share;
    g_coming.right += kick.right * share;
    g_rise = (std::max)(kick.seconds, kShortestRise) / kRiseLags;
    return true;
}

void AimingOverhaul::Recoil::Update(float seconds) {
    g_sinceShot += seconds;
    const Look::Angles left = {Follow(g_coming.up, 0.0f, seconds, g_rise),
                               Follow(g_coming.right, 0.0f, seconds, g_rise)};
    Look::Turn({g_coming.up - left.up, g_coming.right - left.right});
    g_coming = left;
    Shake(seconds);
}

AimingOverhaul::Body::Offset AimingOverhaul::Recoil::Posed() {
    // About the gun's own middle, as two hands fighting to hold it; turning right is a turn left
    // negative.
    return Body::AboutGun(-g_shaken.right, g_shaken.up);
}

void AimingOverhaul::Recoil::Bend(float direction[3]) {
    if (!Stays()) {
        return;
    }
    // The shot leaves where the shaking arms point the gun.
    TurnBy(direction, g_shaken);
    // The shot about to leave is not yet counted in the burst.
    const int past = std::clamp(Burst() + 1 - kSteadyShots, 0, kMostSpreadShots);
    const float spread = kSpreadShare * g_kickUp * static_cast<float>(past);
    if (spread <= 0.0f) {
        return;
    }
    // Noise along the shot only changes its length, which normalising takes out again.
    std::normal_distribution<float> error(0.0f, spread);
    float length = 0.0f;
    for (int i = 0; i < 3; ++i) {
        direction[i] += error(g_random);
        length += direction[i] * direction[i];
    }
    length = std::sqrt(length);
    for (int i = 0; i < 3; ++i) {
        direction[i] /= length;
    }
}
