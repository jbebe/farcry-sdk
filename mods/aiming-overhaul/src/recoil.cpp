// Recoil that stays: the game's own kick for each shot, its size per gun, wear and sights, turned
// into the look, which carries the view, the gun and the shot. A gun whose archetype has
// `AimingOverhaul.RecoilReturns` (written by data\fragments.py) keeps the game's recoil.
#include "recoil.h"

#include "ease.h"
#include "engine/look.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
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

    // A kick rises over the seconds the game gives it, nearly all of it in that time.
    constexpr float kRiseLags = 3.0f;
    constexpr float kShortestRise = 0.01f;

    bool g_enabled = true;
    uint8_t* g_weapon = nullptr;
    bool g_returns = false;

    // The shots in the burst so far, and the seconds since its last.
    int g_burst = 0;
    float g_sinceShot = kBurstGap;

    // The gun's last whole kick up, in radians; the kick still to come and how long it takes.
    float g_kickUp = 0.0f;
    AimingOverhaul::Look::Angles g_coming = {};
    float g_rise = kShortestRise;

    std::minstd_rand g_random{20261102};

    bool Stays() {
        return g_enabled && !g_returns;
    }

    int Burst() {
        return g_sinceShot > kBurstGap ? 0 : g_burst;
    }
}

void AimingOverhaul::Recoil::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void AimingOverhaul::Recoil::FollowWeapon(uint8_t* weapon) {
    if (weapon == g_weapon) {
        return;
    }
    g_weapon = weapon;
    const FCSE_EntityDataAPI* data = FCSE::ApiPointer()->EntityData;
    void* entity = weapon != nullptr ? data->EntityOf(weapon) : nullptr;
    int32_t returns = 0;
    if (entity != nullptr) {
        data->GetInt(entity, "AimingOverhaul.RecoilReturns", &returns);
    }
    g_returns = returns != 0;
}

bool AimingOverhaul::Recoil::Take(const Kick::Kick& kick) {
    if (!Stays()) {
        return false;
    }
    const float share = Burst() < kLightKicks ? kLightShare : 1.0f;
    g_burst = Burst() + 1;
    g_sinceShot = 0.0f;
    g_kickUp = kick.up;
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
}

void AimingOverhaul::Recoil::Bend(float direction[3]) {
    if (!Stays()) {
        return;
    }
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
