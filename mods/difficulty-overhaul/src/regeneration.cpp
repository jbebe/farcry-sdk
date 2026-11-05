// Regeneration: above one health bar, where the heal key's surgery stops, health climbs slowly to
// full, in place of the game's refill of the bar it is in.
#include "engine/combat_log.h"
#include "engine/damage.h"
#include "engine/memory.h"
#include "fcse_api.h"

using namespace DifficultyOverhaul;

namespace {
    using UpdateHealthFn = void(__fastcall*)(uint8_t* counters, void* unused, float seconds);
    using SetHealthFn = void(__fastcall*)(uint8_t* counters, void* unused, float health);

    // CFCXCountersComponentPlayer::UpdateHealth, and SetHealth's vtable slot.
    FCSE::Relocation<UpdateHealthFn> g_updateHealth{FCSE::Uplay(0x006A0E40)};
    UpdateHealthFn g_originalUpdateHealth = nullptr;
    constexpr ptrdiff_t kSetHealthSlot = 0xC0;

    // CFCXCountersComponentPlayer.
    constexpr ptrdiff_t kBarSize = 0x6C;
    constexpr ptrdiff_t kSinceLastDamage = 0x70;
    constexpr ptrdiff_t kRegenDelay = 0x74;
    // The game's refill of the bar health is in, per second.
    constexpr ptrdiff_t kBarRefill = 0x78;
    // What UpdateHealth changed health by this frame, per second: the refill, the last bar bleeding
    // out, or drowning.
    constexpr ptrdiff_t kHealthRate = 0x7C;

    // Hardcore: seconds from one bar to full health.
    constexpr float kOneBarToFullSeconds = 300.0f;

    bool g_climbing = false;

    void __fastcall UpdateHealthDetour(uint8_t* counters, void* unused, float seconds) {
        const float refill = Read<float>(counters, kBarRefill);
        Write(counters, kBarRefill, 0.0f);
        g_originalUpdateHealth(counters, unused, seconds);
        Write(counters, kBarRefill, refill);

        const float health = Damage::Health(counters);
        const float max = Damage::MaxHealth(counters);
        const float bar = Read<float>(counters, kBarSize);
        const bool climbing =
            health >= bar && health < max && Read<float>(counters, kHealthRate) == 0.0f &&
            Read<float>(counters, kSinceLastDamage) >= Read<float>(counters, kRegenDelay);
        if (climbing) {
            const float rate = (max - bar) / kOneBarToFullSeconds;
            if (!g_climbing) {
                CombatLogf("regenerating: health %.0f of %.0f, one bar %.0f, full in %.0f s",
                           health, max, bar, (max - health) / rate);
            }
            // The counter caps it at maximum health.
            Read<SetHealthFn>(Read<const void*>(counters, 0), kSetHealthSlot)(
                counters, nullptr, health + rate * seconds);
        }
        g_climbing = climbing;
    }
}

void InstallRegeneration() {
    if (!Hook(g_updateHealth, &UpdateHealthDetour, &g_originalUpdateHealth)) {
        FCSE::Logf("regeneration: the player's health update was not found in this build");
    }
}
