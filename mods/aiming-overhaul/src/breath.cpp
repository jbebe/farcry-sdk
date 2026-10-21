// Holding the breath while aiming: sprint held down the sights steadies the sway for a few seconds,
// then the hands shake harder until it is let go, and the player is winded for a while after.
#include "breath.h"

#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>

namespace {
    // The keyboard's sprint, sent every frame the key is held, and its release.
    constexpr uint32_t kSprint = FCSE::Crc32("lock_sprint");
    constexpr uint32_t kSprintReleased = FCSE::Crc32("unlock_sprint");
    // Seconds without a sprint signal that count as let go, should the release be lost.
    constexpr float kLetGo = 0.5f;

    // Seconds the breath holds, and to get all of it back.
    constexpr float kLongest = 4.0f;
    constexpr float kRecover = 3.0f;
    // The sway left while held; the most past the breath's end, reached over kStrain seconds; and
    // the most while winded, which eases off as the breath comes back.
    constexpr float kHeld = 0.15f;
    constexpr float kStrained = 2.5f;
    constexpr float kStrain = 2.0f;
    constexpr float kWinded = 1.5f;
    // Seconds for the sway to follow.
    constexpr float kFollow = 0.25f;

    std::atomic<bool> g_enabled{true};
    bool g_released = true;
    float g_sinceSprint = kLetGo;
    float g_breath = kLongest;
    float g_strain = 0.0f;
    float g_steadiness = 1.0f;
}

void AimingOverhaul::Breath::OnSignal(uint32_t signal) {
    if (signal == kSprint) {
        g_released = false;
        g_sinceSprint = 0.0f;
    } else if (signal == kSprintReleased) {
        g_released = true;
    }
}

void AimingOverhaul::Breath::Update(float seconds, bool aiming) {
    g_sinceSprint += seconds;
    const bool holding = g_enabled && aiming && !g_released && g_sinceSprint < kLetGo;
    float target;
    if (holding && g_breath > 0.0f) {
        g_breath = (std::max)(g_breath - seconds, 0.0f);
        target = kHeld;
    } else if (holding) {
        g_strain = (std::min)(g_strain + seconds, kStrain);
        target = 1.0f + (kStrained - 1.0f) * g_strain / kStrain;
    } else {
        g_strain = 0.0f;
        g_breath = (std::min)(g_breath + seconds * kLongest / kRecover, kLongest);
        target = 1.0f + (kWinded - 1.0f) * (1.0f - g_breath / kLongest);
    }
    g_steadiness += (target - g_steadiness) * (1.0f - std::exp(-seconds / kFollow));
}

float AimingOverhaul::Breath::Steadiness() {
    return g_steadiness;
}

bool AimingOverhaul::Breath::HoldsSprint() {
    return g_enabled;
}

void AimingOverhaul::Breath::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
