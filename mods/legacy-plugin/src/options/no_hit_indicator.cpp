// No hit indicator: taking damage no longer shows the directional hit indicators.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/gameplay/hitindicator.ixx, which
// skips the loop raising them.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoHitIndicator() {
    FCSE::Logf("no hit indicator: not implemented yet");
}

void __cdecl OnNoHitIndicatorChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
