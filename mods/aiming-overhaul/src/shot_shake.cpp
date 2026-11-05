// The view shaken by the player's shots. See shot_shake.h.
#include "shot_shake.h"

#include "ease.h"
#include "engine/aim.h"
#include "engine/fire.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <numbers>

namespace {
    constexpr float kPi = std::numbers::pi_v<float>;
    constexpr float kDegree = kPi / 180.0f;

    // A shot kicks the view up by kKickShare of the gun's kick, eased toward kMostKick, on a spring
    // at kKickOmega damped not to swing past rest: it peaks after 0.04 s and is back in 0.25 s.
    constexpr float kKickShare = 1.5f;
    constexpr float kMostKick = 3.0f * kDegree;
    constexpr float kKickOmega = 25.0f;

    // Full-auto rounds less than kBurstGap seconds apart are one burst. Over its first
    // kRoundsToRoll rounds the kick fades into a roll of kRollShare of it either way, a whole cycle
    // every kRoundsPerCycle rounds, eased in and out over kRollEase seconds. It stops once a round
    // is kLate times the burst's interval late.
    constexpr float kBurstGap = 0.25f;
    constexpr float kRoundsToRoll = 3.0f;
    constexpr float kRollShare = 0.75f;
    constexpr float kRoundsPerCycle = 4.0f;
    constexpr float kRollEase = 0.06f;
    constexpr float kLate = 1.5f;
    constexpr float kShortestInterval = 0.02f;

    std::atomic<bool> g_enabled{true};
    uint8_t* g_weapon = nullptr;
    bool g_fullAuto = false;

    // The burst's rounds before the last, the seconds since the last and between the last two.
    int g_rounds = 0;
    float g_since = kBurstGap;
    float g_interval = kBurstGap;

    AimingOverhaul::Spring g_kick;
    // The roll's size either way as the last round set it, and as eased.
    float g_rollSize = 0.0f;
    float g_roll = 0.0f;
}

void AimingOverhaul::ShotShake::FollowWeapon(uint8_t* weapon) {
    if (weapon != g_weapon) {
        g_weapon = weapon;
        g_fullAuto = Fire::FullAuto(weapon);
    }
}

void AimingOverhaul::ShotShake::Kick(const Kick::Kick& kick) {
    if (g_fullAuto && g_since < kBurstGap) {
        g_rounds++;
        g_interval = (std::max)(g_since, kShortestInterval);
    } else {
        g_rounds = 0;
    }
    g_since = 0.0f;

    const float size = SoftLimit(kKickShare * (std::max)(kick.up, 0.0f), kMostKick);
    const float rolled = (std::min)(static_cast<float>(g_rounds) / kRoundsToRoll, 1.0f);
    // A damped spring struck at rest peaks at its speed over omega times e.
    g_kick.v += (1.0f - rolled) * size * kKickOmega * std::numbers::e_v<float>;
    g_rollSize = rolled * kRollShare * size;
}

AimingOverhaul::ShotShake::Turn AimingOverhaul::ShotShake::Update(float seconds) {
    g_since += seconds;
    const bool rolling = g_rounds > 0 && g_since < kLate * g_interval;
    g_roll = Follow(g_roll, rolling ? g_rollSize : 0.0f, seconds, kRollEase);
    g_kick.Step(0.0f, kKickOmega, 1.0f, seconds);
    if (!g_enabled) {
        return {};
    }
    const float cycle = 2.0f * kPi * (static_cast<float>(g_rounds) + g_since / g_interval) /
                        kRoundsPerCycle;
    const float free = 1.0f - Aim::Scoped();
    return {free * g_kick.x, free * g_roll * std::sin(cycle)};
}

void AimingOverhaul::ShotShake::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
