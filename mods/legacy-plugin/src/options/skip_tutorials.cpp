// Skip tutorial pop-ups: the tutorial boxes that interrupt play, or those and the floating hints.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/tutorialmessages.ixx. The
// choice indexes kSkipTutorialsChoices in main.cpp.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallSkipTutorials() {
    FCSE::Logf("skip tutorial pop-ups: not implemented yet");
}

void __cdecl OnSkipTutorialsChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
