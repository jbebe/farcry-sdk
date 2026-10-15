// Briefings keep control: an NPC's briefing no longer stands the player up and takes control.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/skippable-dialog.md (its Dunia.dll half).
// The patch turns the stand-up call in a CPawnAgent event handler's briefing branch into a jump
// straight to the "briefing" send.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallBriefingControl() {
    FCSE::Logf("briefings keep control: not implemented yet");
}

void __cdecl OnBriefingControlChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
