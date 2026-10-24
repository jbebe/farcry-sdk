// Holding the breath while aiming a gun that needs it: sprint held down its scope steadies the sway
// for a few seconds, then the hands shake harder until it is let go, and the player is winded for a
// while after.
#include "breath.h"

#include "ease.h"
#include "engine/sound.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <initializer_list>

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

    // The breath drawn as the hold begins and let out as it ends, weaker while stamina is above
    // kTired and stronger below: the layer's banks, written by data\breath.py, played as the
    // player's own voice.
    constexpr uint32_t kInhale = 0x00FC0B00;
    constexpr uint32_t kTiredInhale = 0x00FC0B20;
    constexpr uint32_t kExhale = 0x00FC0B40;
    constexpr uint32_t kTiredExhale = 0x00FC0B60;
    constexpr float kTired = 0.5f;
    constexpr int32_t kOnoPlayer = 6;

    std::atomic<bool> g_enabled{true};
    // The weapon in hand as last followed, and whether its breath can be held.
    uint8_t* g_weapon = nullptr;
    std::atomic<bool> g_needed{false};
    float g_sinceSprint = kLetGo;
    float g_breath = kLongest;
    float g_strain = 0.0f;
    float g_steadiness = 1.0f;
    bool g_holding = false;
}

void AimingOverhaul::Breath::OnSignal(uint32_t signal) {
    if (signal == kSprint) {
        g_sinceSprint = 0.0f;
    } else if (signal == kSprintReleased) {
        g_sinceSprint = kLetGo;
    }
}

void AimingOverhaul::Breath::FollowWeapon(uint8_t* weapon) {
    if (weapon == g_weapon) {
        return;
    }
    g_weapon = weapon;
    const FCSE_EntityDataAPI* data = FCSE::ApiPointer()->EntityData;
    void* entity = weapon != nullptr ? data->EntityOf(weapon) : nullptr;
    int32_t needed = 0;
    if (entity != nullptr) {
        data->GetInt(entity, "AimingOverhaul.HoldBreath", &needed);
    }
    g_needed = needed != 0;
}

bool AimingOverhaul::Breath::Needed() {
    return g_needed;
}

void AimingOverhaul::Breath::Update(float seconds, bool aiming, float stamina) {
    for (uint32_t bank : {kInhale, kTiredInhale, kExhale, kTiredExhale}) {
        Sound::Hold(bank);
    }
    g_sinceSprint += seconds;
    const bool holding = g_enabled && g_needed && aiming && g_sinceSprint < kLetGo;
    if (holding != g_holding) {
        g_holding = holding;
        const bool tired = stamina < kTired;
        Sound::Play(holding ? (tired ? kTiredInhale : kInhale) : (tired ? kTiredExhale : kExhale),
                    kOnoPlayer);
    }
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
    g_steadiness = Follow(g_steadiness, target, seconds, kFollow);
}

float AimingOverhaul::Breath::Steadiness() {
    return g_steadiness;
}

bool AimingOverhaul::Breath::HoldsSprint() {
    return g_enabled && g_needed;
}

void AimingOverhaul::Breath::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
