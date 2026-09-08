// Unlock all weapons.
//
// The `cheat_AllWeaponsUnlock` console command sets one profile flag, and its own catalog entry
// admits what that buys: only the weapons the current map offers. The flag bypasses the per-weapon
// unlock list and nothing else, and the list is not what is holding most of the bazaar back.
//
// CWeaponBazaar::IsWeaponUnlocked tests the weapon's act tag twice on the way to that flag, against
// 1 and then 2, and that is what keeps act-2 and act-3 stock out of an act-1 bazaar. Writing an act
// tag no weapon carries over each compare's immediate makes both jumps unconditional and takes the
// test out of the path - one byte per site, with the branch displacements untouched, so both jumps
// still land where they did. See docs/docs/engine-internals/cheat-flags-and-economy.md.
#include "engine/game_profile.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    // An act tag no compare can ever match, since the tag is only ever 0, 1 or 2.
    constexpr uint8_t kNeverMatches = 0xFF;

    // Both compares, as where each immediate sits inside the match and what ships there.
    FCSE::Relocation<uint8_t*> g_actGates{
        FCSE::Pattern("80 7E 58 01 75 09 E8 ?? ?? ?? ?? 84 C0 74 11 80 7E 58 02")};

    struct Gate {
        size_t immediateOffset;
        uint8_t shipped;
    };

    constexpr Gate kGates[] = {{3, 0x01}, {18, 0x02}};

    bool g_enabled = false;

    bool WriteActGates(bool unlocked) {
        const FCSE_PluginAPI* api = FCSE::ApiPointer();

        if (!g_actGates) {
            return false;
        }

        bool all = true;
        for (const Gate& gate : kGates) {
            const uint8_t value = unlocked ? kNeverMatches : gate.shipped;
            if (!api->Patch(g_actGates.get() + gate.immediateOffset, &value, 1)) {
                all = false;
            }
        }
        return all;
    }
}

int GetUnlockAllWeapons() { return g_enabled ? 1 : 0; }

void SetUnlockAllWeapons(int value) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const bool unlocked = value != 0;
    if (unlocked == g_enabled) {
        return;
    }

    g_enabled = unlocked;
    DevTools::GameProfile::Want(DevTools::GameProfile::Cheat::AllWeaponsUnlock, unlocked);

    const bool bothGates = WriteActGates(unlocked);

    // Disabling with the gates unresolved needs no warning: gates that never resolved were never
    // patched, and the profile flag is put back either way.
    const char* line = "unlock all weapons: off - the bazaar stocks what the act allows";
    if (unlocked) {
        line = bothGates ? "unlock all weapons: on - the bazaar stocks every act"
                         : "unlock all weapons: on, but the act gates were not found in this build - "
                           "only the weapons the current act offers";
    }
    api->Log(line);
}
