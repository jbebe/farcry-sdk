// No rim lighting: the glow outlining characters, mainly visible at night, is gone.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/graphics/rimlighting.ixx, which
// credits Boggalog's Far Cry 2 Patched. It zeroes the rim light intensity after the per-frame
// shader parameter fill.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoRimLight() {
    FCSE::Logf("no rim lighting: not implemented yet");
}

void __cdecl OnNoRimLightChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
