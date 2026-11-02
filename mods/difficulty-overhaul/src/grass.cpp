// Grass: high grass hides a crouched player only from 30 m out, in any state: soldiers within 30 m see
// him as in the open, where the game has grass hide him from 3 m and stop as soon as a soldier fights.
#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // CGrassEvaluator::ComputeVisibility's concealment: its ramp with distance, whose full and start
    // distances are operands kFullOperand and kFromOperand bytes in, then its factor by the soldier's
    // state, whose in-combat factor is kCombatOperand bytes in.
    FCSE::Relocation<uint8_t*> g_concealment{
        FCSE::Pattern("D9 05 ?? ?? ?? ?? 83 EC 08 D9 5C 24 04 8B CE D9 05 ?? ?? ?? ?? D9 1C 24 E8 ?? ?? "
                      "?? ?? 51 8B CF D9 1C 24 E8 ?? ?? ?? ?? D9 5C 24 0C 8B 47 24 80 78 3E 00 75 ?? 80 "
                      "78 3F 00 75 ?? 80 78 3D 00 F3 0F 10 4C 24 ?? F3 0F 10 05 ?? ?? ?? ?? 74 ?? F3 0F "
                      "59 0D ?? ?? ?? ?? EB ?? F3 0F 59 0D ?? ?? ?? ?? EB ?? F3 0F 10 4C 24 ?? F3 0F 59 "
                      "0D")};
    constexpr ptrdiff_t kFullOperand = 2;
    constexpr ptrdiff_t kFromOperand = 17;
    constexpr ptrdiff_t kCombatOperand = 109;

    // Hardcore: where grass starts hiding the player and where it hides him in full, in place of the
    // game's 3 and 10 m.
    float g_concealmentFrom = 30.0f;
    float g_concealmentFull = 40.0f;
    // Hardcore: how much of the player high grass hides from a soldier in combat, as much as from an
    // idle one.
    float g_combatConcealment = 0.9f;
}

void InstallGrass() {
    if (!RedirectOperand(g_concealment, kFromOperand, &g_concealmentFrom) ||
        !RedirectOperand(g_concealment, kFullOperand, &g_concealmentFull) ||
        !RedirectOperand(g_concealment, kCombatOperand, &g_combatConcealment)) {
        FCSE::Logf("grass: the grass concealment was not found in this build");
    }
}
