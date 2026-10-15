// Sprint turn: on, sprinting no longer lowers horizontal look speed.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/input/looksensitivity.ixx, its Sprint
// Turn Modifier: on overrides the archetype's sprint yaw modifier with 1.0 at both sites that apply
// it.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallSprintTurn() {
    FCSE::Logf("sprint turn: not implemented yet");
}

void __cdecl OnSprintTurnChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
