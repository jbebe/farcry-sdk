// Sprint toggle: a tap on sprint keeps running until the next press or the player stops. Keyboard
// only - the pad's SprintLock already toggles.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/input/inputtoggles.ixx.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallSprintToggle() {
    FCSE::Logf("sprint toggle: not implemented yet");
}

void __cdecl OnSprintToggleChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
