// No forced buddy rescues: factions stop assigning buddy rescue missions, so a bugged one cannot
// block the story.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/buddy-rescue-optional.md. The patch skips
// registering CBuddyMission's Active and the mission manager's MissionsSinceBuddyUnlock. That
// changes what a save holds, so the row asks for a new game.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoForcedRescue() {
    FCSE::Logf("no forced buddy rescues: not implemented yet");
}

void __cdecl OnNoForcedRescueChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
