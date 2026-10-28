// Healing: the surgery on the last bar leaves the player one rifle hit from death and a syrette
// two; only full health takes four, and it comes back slowly, from the syrette's level up.
#include "engine/damage.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>

using namespace DifficultyOverhaul;

namespace {
    using UpdateHealthFn = void(__fastcall*)(uint8_t* counters, void* unused, float seconds);
    using ProcessHealFn = void(__fastcall*)(uint8_t* counters);
    using CanHealSelfFn = bool(__fastcall*)(uint8_t* counters);

    // CFCXCountersComponentPlayer::UpdateHealth, ProcessHeal (the heal key's surgery or syrette)
    // and CanHealSelf.
    FCSE::Relocation<UpdateHealthFn> g_updateHealth{FCSE::Uplay(0x006A0E40)};
    FCSE::Relocation<ProcessHealFn> g_processHeal{FCSE::Uplay(0x006A00E0)};
    FCSE::Relocation<CanHealSelfFn> g_canHealSelf{FCSE::Uplay(0x006A2CA0)};
    UpdateHealthFn g_originalUpdateHealth = nullptr;
    ProcessHealFn g_originalProcessHeal = nullptr;
    CanHealSelfFn g_originalCanHealSelf = nullptr;

    // CFCXCountersComponentPlayer; the refill tops up the bar health is in, per second.
    constexpr ptrdiff_t kBarSize = 0x6C;
    constexpr ptrdiff_t kSinceLastDamage = 0x70;
    constexpr ptrdiff_t kRegenDelay = 0x74;
    constexpr ptrdiff_t kBarRefill = 0x78;
    constexpr ptrdiff_t kHealthRate = 0x7C;

    // Hardcore: a rifleman's torso hit at 20 m, and what the surgery and a syrette leave.
    constexpr float kRifleHit = 264.0f;
    constexpr float kSurgeryHealth = 1.5f * kRifleHit;
    constexpr float kSyretteHealth = 2.5f * kRifleHit;
    constexpr float kSyretteToFullSeconds = 360.0f;

    void __fastcall UpdateHealthDetour(uint8_t* counters, void* unused, float seconds) {
        Write(counters, kBarRefill, 0.0f);
        g_originalUpdateHealth(counters, unused, seconds);

        const float health = Damage::Health(counters);
        const float max = Damage::MaxHealth(counters);
        // A negative rate is the last bar bleeding out, or drowning.
        if (health < kSyretteHealth || health >= max || Read<float>(counters, kHealthRate) < 0.0f ||
            Read<float>(counters, kSinceLastDamage) < Read<float>(counters, kRegenDelay)) {
            return;
        }
        const float rate = (max - kSyretteHealth) / kSyretteToFullSeconds;
        Damage::SetHealth(counters, health + rate * seconds);
    }

    void __fastcall ProcessHealDetour(uint8_t* counters) {
        const float before = Damage::Health(counters);
        const bool surgery = before < Read<float>(counters, kBarSize);
        g_originalProcessHeal(counters);
        Damage::SetHealth(counters, std::max(before, surgery ? kSurgeryHealth : kSyretteHealth));
        FCSE::Logf("healed: %s, health %.0f -> %.0f", surgery ? "surgery" : "syrette", before,
                   Damage::Health(counters));
    }

    // A syrette is refused where it would not raise health.
    bool __fastcall CanHealSelfDetour(uint8_t* counters) {
        return Damage::Health(counters) < kSyretteHealth && g_originalCanHealSelf(counters);
    }
}

void InstallHealing() {
    if (!Hook(g_updateHealth, &UpdateHealthDetour, &g_originalUpdateHealth) ||
        !Hook(g_processHeal, &ProcessHealDetour, &g_originalProcessHeal) ||
        !Hook(g_canHealSelf, &CanHealSelfDetour, &g_originalCanHealSelf)) {
        FCSE::Logf("healing: the player's health update or heal was not found in this build");
    }
}
