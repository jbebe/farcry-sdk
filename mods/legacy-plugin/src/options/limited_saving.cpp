// Limited saving: Save Game leaves the pause menu and F5/F9 stop working; save points still save.
//
// From Realism Plus, legacy/realism-plus/features/saving-no-pause-save.md, which blanks
// CSaveGamePage and PAUSE_SAVEGAME, and FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482)
// source/gameplay/limitedsaving.ixx, which drops the pause entry live and refuses quicksave and
// quickload.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallLimitedSaving() {
    FCSE::Logf("limited saving: not implemented yet");
}

void __cdecl OnLimitedSavingChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
