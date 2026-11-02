// Camo suit: crouched and still in high grass, the suit hides the player from a soldier who is not
// fighting from 10 m out, where the game has it from 2 m, or 4 m once the soldier is alerted.
#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // CGrassEvaluator::ComputeVisibility's camouflage, whose distances past which the suit hides the
    // player from an alerted and from an idle soldier are operands kAlertedOperand and kIdleOperand
    // bytes in.
    FCSE::Relocation<uint8_t*> g_camouflage{
        FCSE::Pattern("80 7B 2B 00 74 ?? 8B 4F 24 80 79 3E 00 75 ?? 80 79 3F 00 75 ?? 80 79 3D 00 74 ?? "
                      "E8 ?? ?? ?? ?? D9 05 ?? ?? ?? ?? D9 C9 DF F1 DD D8 77 ?? 8B 4F 24 80 79 3D 00 75 "
                      "?? E8 ?? ?? ?? ?? D9 05")};
    constexpr ptrdiff_t kAlertedOperand = 34;
    constexpr ptrdiff_t kIdleOperand = 62;

    // Hardcore: how far out the suit hides a still player, from an alerted or an idle soldier.
    float g_camouflageFrom = 10.0f;
}

void InstallCamoSuit() {
    if (!RedirectOperand(g_camouflage, kAlertedOperand, &g_camouflageFrom) ||
        !RedirectOperand(g_camouflage, kIdleOperand, &g_camouflageFrom)) {
        FCSE::Logf("camo suit: the suit's camouflage was not found in this build");
    }
}
