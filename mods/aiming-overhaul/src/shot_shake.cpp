// The view shaken by the player's shots. See shot_shake.h.
#include "shot_shake.h"

#include "cartridge.h"
#include "ease.h"
#include "engine/aim.h"
#include "engine/fire.h"
#include "tuning.h"

#include "imgui.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <numbers>

namespace {
    constexpr float kPi = std::numbers::pi_v<float>;
    using AimingOverhaul::kDegree;

    // The push, in newton-seconds, past which the shake eases off.
    constexpr float kMostImpulse = 15.0f;
    // Newton-seconds counted per radian of the game's kick where the gun names no round.
    constexpr float kImpulsePerKick = 5.65f / 0.0192f;
    // How many times its seconds to peak a struck spring takes to be within a twentieth of rest.
    constexpr float kSettledAfterPeaks = 5.74f;

    // Full-auto rounds less than kBurstGap seconds apart are one burst. The roll eases in and out
    // over kRollEase seconds and stops once a round is kLate times the burst's interval late.
    constexpr float kBurstGap = 0.25f;
    constexpr float kRollEase = 0.06f;
    constexpr float kLate = 1.5f;
    constexpr float kShortestInterval = 0.02f;

    std::atomic<bool> g_enabled{true};
    uint8_t* g_weapon = nullptr;
    bool g_fullAuto = false;

    // The burst's rounds before the last, the seconds since its first, since the last and between
    // the last two.
    int g_rounds = 0;
    float g_burst = 0.0f;
    float g_since = kBurstGap;
    float g_interval = kBurstGap;

    AimingOverhaul::Spring g_kick;
    // Kicks waiting out the delay: the seconds left and how far each kicks.
    struct Waiting {
        float seconds;
        float peak;
    };
    std::array<Waiting, 16> g_waiting = {};
    size_t g_waitingCount = 0;
    // The roll's size either way as the last round set it, and as eased.
    float g_rollSize = 0.0f;
    float g_roll = 0.0f;
    // The last shot's push, in newton-seconds.
    std::atomic<float> g_lastImpulse{0.0f};

    float KickOmega() {
        return kSettledAfterPeaks / AimingOverhaul::Tuning::InHand().kickTime;
    }

    // Radians a push of `impulse` newton-seconds turns the view by at `perImpulse` degrees each.
    float Angle(float impulse, float perImpulse) {
        return perImpulse * kDegree * AimingOverhaul::SoftLimit(impulse, kMostImpulse);
    }
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
        g_burst = 0.0f;
    }
    g_since = 0.0f;

    const float round = Cartridge::Impulse();
    const float impulse = round > 0.0f ? round : (std::max)(kick.up, 0.0f) * kImpulsePerKick;
    g_lastImpulse = impulse;
    // A burst's first round only kicks; past the fade's seconds into it, its rounds only roll.
    const Tuning::Values& values = Tuning::InHand();
    const float rolled =
        g_rounds == 0 ? 0.0f : (std::min)(g_burst / (std::max)(values.rollFade, 1e-3f), 1.0f);
    const float peak = (1.0f - rolled) * Angle(impulse, values.kickPerImpulse);
    if (values.kickDelay > 0.0f && g_waitingCount < g_waiting.size()) {
        g_waiting[g_waitingCount++] = {values.kickDelay, peak};
    } else {
        g_kick.Strike(peak, KickOmega());
    }
    g_rollSize = rolled * Angle(impulse, values.rollPerImpulse);
}

AimingOverhaul::ShotShake::Turn AimingOverhaul::ShotShake::Update(float seconds) {
    g_since += seconds;
    g_burst += seconds;
    for (size_t i = 0; i < g_waitingCount;) {
        g_waiting[i].seconds -= seconds;
        if (g_waiting[i].seconds > 0.0f) {
            ++i;
            continue;
        }
        g_kick.Strike(g_waiting[i].peak, KickOmega());
        g_waiting[i] = g_waiting[--g_waitingCount];
    }
    const bool rolling = g_rounds > 0 && g_since < kLate * g_interval;
    g_roll = Follow(g_roll, rolling ? g_rollSize : 0.0f, seconds, kRollEase);
    g_kick.Settle(KickOmega(), seconds);
    if (!g_enabled) {
        return {};
    }
    const float free = 1.0f - Aim::Scoped();
    return {free * g_kick.x,
            free * g_roll * std::sin(2.0f * kPi * g_burst / Tuning::InHand().rollCycle)};
}

void AimingOverhaul::ShotShake::DrawLastShot() {
    ImGui::Separator();
    ImGui::Text("In hand: %s", Aim::WeaponName());
    const float last = g_lastImpulse;
    if (Cartridge::Impulse() > 0.0f) {
        ImGui::Text("Fires %s: %.2f N s a round", Cartridge::Name(), Cartridge::Impulse());
    } else {
        ImGui::Text("Names no round: its last shot's kick counted as %.2f N s", last);
    }
    ImGui::Text("Last shot: a kick of %.2f deg, a roll of %.2f deg either way",
                Angle(last, Tuning::InHand().kickPerImpulse) / kDegree,
                Angle(last, Tuning::InHand().rollPerImpulse) / kDegree);
}

void AimingOverhaul::ShotShake::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
