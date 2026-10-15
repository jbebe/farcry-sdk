// Keep weapon on rescue: a buddy rescue no longer swaps the player's secondary weapon for a pistol.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/buddy-rescue-fluid.md (its Dunia.dll half;
// the short rescue is data). The patch repoints CGOStateEventRescue::Activate's "try drop weapons"
// jump-table entry at a handler that does nothing for the player.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallRescueKeepsWeapon() {
    FCSE::Logf("keep weapon on rescue: not implemented yet");
}

void __cdecl OnRescueKeepsWeaponChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
