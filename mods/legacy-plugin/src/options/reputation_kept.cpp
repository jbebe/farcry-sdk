// No reputation drop: completing A1SM03 or A2SM08 no longer lowers the player's reputation.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/reputation-kept.md. The patch repoints both
// mission-name tests in CFCXMissionManager::MissionCompleted at "A3SM16", which then takes the
// A1SM03 reset itself.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallReputationKept() {
    FCSE::Logf("no reputation drop: not implemented yet");
}

void __cdecl OnReputationKeptChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
