// Grass: a player crouched still in high grass stays hidden from a soldier in combat, where the game
// stops grass hiding the player as soon as the soldier is fighting.
#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // CGrassEvaluator::ComputeVisibility's concealment by the soldier's state; the in-combat factor's
    // operand is kCombatOperand bytes in.
    FCSE::Relocation<uint8_t*> g_concealmentChoice{
        FCSE::Pattern("8B 47 24 80 78 3E 00 75 ?? 80 78 3F 00 75 ?? 80 78 3D 00 F3 0F 10 4C 24 ?? "
                      "F3 0F 10 05 ?? ?? ?? ?? 74 ?? F3 0F 59 0D ?? ?? ?? ?? EB ?? F3 0F 59 0D ?? ?? "
                      "?? ?? EB ?? F3 0F 10 4C 24 ?? F3 0F 59 0D")};
    constexpr ptrdiff_t kCombatOperand = 65;

    // Hardcore: how much of the player high grass hides from a soldier in combat, from 10 m out.
    float g_combatConcealment = 0.8f;
}

void InstallGrass() {
    if (!RedirectOperand(g_concealmentChoice, kCombatOperand, &g_combatConcealment)) {
        FCSE::Logf("grass: the grass concealment in combat was not found in this build");
    }
}
