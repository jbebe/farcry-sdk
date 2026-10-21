// Grass: a player crouched still in high grass stays hidden from a soldier in combat, where the game
// stops grass hiding the player as soon as the soldier is fighting.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // CGrassEvaluator::ComputeVisibility's choice of concealment by the soldier's state: the alerted
    // and idle factors, then the in-combat one, whose MULSS reads it from a constant shared with
    // other code; that operand starts kCombatOperand bytes in.
    FCSE::Relocation<uint8_t*> g_concealmentChoice{
        FCSE::Pattern("8B 47 24 80 78 3E 00 75 ?? 80 78 3F 00 75 ?? 80 78 3D 00 F3 0F 10 4C 24 ?? "
                      "F3 0F 10 05 ?? ?? ?? ?? 74 ?? F3 0F 59 0D ?? ?? ?? ?? EB ?? F3 0F 59 0D ?? ?? "
                      "?? ?? EB ?? F3 0F 10 4C 24 ?? F3 0F 59 0D")};
    constexpr ptrdiff_t kCombatOperand = 65;

    // Hardcore: how much of the player high grass hides from a soldier in combat, from 10 m out. The
    // game's is 0, against 0.765 for an alerted soldier and 0.9 for an idle one.
    const float kCombatConcealment = 0.8f;
}

void InstallGrass() {
    const float* concealment = &kCombatConcealment;
    if (!g_concealmentChoice ||
        !FCSE::ApiPointer()->Patch(g_concealmentChoice.get() + kCombatOperand, &concealment,
                                   sizeof(concealment))) {
        FCSE::Logf("grass: the grass concealment in combat was not found in this build");
    }
}
