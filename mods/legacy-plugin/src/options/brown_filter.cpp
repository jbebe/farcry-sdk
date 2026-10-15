// Brown filter: on is the game's own grade, off a neutral one without the brown-orange remap.
//
// The neutral values (remap and contrast 0, saturation 1) are Scubrah's Patch's,
// legacy/scubrahs-patch/features/original-colorgrading.md; the hook on the adaptive bloom pass's
// settings is FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/graphics/saturation.ixx.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallBrownFilter() {
    FCSE::Logf("brown filter: not implemented yet");
}

void __cdecl OnBrownFilterChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
