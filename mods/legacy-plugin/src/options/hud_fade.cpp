// HUD fade-out: how long the health, ammo and equipment counters stay up, or always hidden/shown.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/hudfadeout.ixx. The choice
// indexes kHudFadeChoices in main.cpp; JackalFix's values for it are -2, 1, 2, 0, 5, 8, 10, 15, 20,
// 30, 60, -1.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallHudFade() {
    FCSE::Logf("HUD fade-out: not implemented yet");
}

void __cdecl OnHudFadeChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
