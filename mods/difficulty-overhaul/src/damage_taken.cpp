// Damage taken: the last health bar quarters a hit on Easy and Normal only.
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

using namespace DifficultyOverhaul;

namespace {
    using LastBarFn = void(__fastcall*)(void* counters, void* unused, float* damage);

    // CFCXCountersComponentPlayerSP::ApplyDamageMultiplier.
    FCSE::Relocation<LastBarFn> g_lastBar{
        FCSE::Pattern("E8 ?? ?? ?? ?? 84 C0 74 14 8B 44 24 04 F3 0F 10 00 F3 0F 59 05 ?? ?? ?? ?? "
                      "F3 0F 11 00 C2 04 00")};
    LastBarFn g_originalLastBar = nullptr;

    void __fastcall LastBarDetour(void* counters, void* unused, float* damage) {
        if (Game::CurrentDifficulty() < Game::kHardcore) {
            g_originalLastBar(counters, unused, damage);
        }
    }
}

void InstallDamageTaken() {
    if (!Hook(g_lastBar, &LastBarDetour, &g_originalLastBar)) {
        FCSE::Logf("damage taken: the last health bar's damage multiplier was not found in this "
                   "build");
    }
}
