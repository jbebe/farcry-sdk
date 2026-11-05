// Damage taken: the last health bar no longer quarters the damage.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // CFCXCountersComponentPlayerSP::ApplyDamageMultiplier, which quarters the damage while health
    // is in the last bar; patched to return at once.
    FCSE::Relocation<uint8_t*> g_lastBarMultiplier{
        FCSE::Pattern("E8 ?? ?? ?? ?? 84 C0 74 14 8B 44 24 04 F3 0F 10 00 F3 0F 59 05 ?? ?? ?? ?? "
                      "F3 0F 11 00 C2 04 00")};
    constexpr uint8_t kReturn4[] = {0xC2, 0x04, 0x00};
}

void InstallDamageTaken() {
    if (!g_lastBarMultiplier ||
        !FCSE::ApiPointer()->Patch(g_lastBarMultiplier, kReturn4, sizeof(kReturn4))) {
        FCSE::Logf("damage taken: the last health bar's damage multiplier was not found in this "
                   "build");
    }
}
