// Healing: on Infamous the bar health is in never refills.
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

using namespace DifficultyOverhaul;

namespace {
    using UpdateHealthFn = void(__fastcall*)(uint8_t* counters, void* unused, float seconds);

    // CFCXCountersComponentPlayer::UpdateHealth.
    FCSE::Relocation<UpdateHealthFn> g_updateHealth{FCSE::Uplay(0x006A0E40)};
    UpdateHealthFn g_originalUpdateHealth = nullptr;

    // CFCXCountersComponentPlayer: the game's refill of the bar health is in, per second.
    constexpr ptrdiff_t kBarRefill = 0x78;

    void __fastcall UpdateHealthDetour(uint8_t* counters, void* unused, float seconds) {
        if (Game::CurrentDifficulty() != Game::kInfamous) {
            g_originalUpdateHealth(counters, unused, seconds);
            return;
        }
        const float refill = Read<float>(counters, kBarRefill);
        Write(counters, kBarRefill, 0.0f);
        g_originalUpdateHealth(counters, unused, seconds);
        Write(counters, kBarRefill, refill);
    }
}

void InstallHealing() {
    if (!Hook(g_updateHealth, &UpdateHealthDetour, &g_originalUpdateHealth)) {
        FCSE::Logf("healing: the player's health update was not found in this build");
    }
}
