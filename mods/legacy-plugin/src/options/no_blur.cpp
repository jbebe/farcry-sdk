// No sprint and aim blur: sprinting and aiming down the sights no longer blur the screen.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/graphics/postfx.ixx, which skips
// CScenePostFxBlur::Render.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoBlur() {
    FCSE::Logf("no sprint and aim blur: not implemented yet");
}

void __cdecl OnNoBlurChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
