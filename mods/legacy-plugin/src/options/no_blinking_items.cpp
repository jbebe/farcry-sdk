// No blinking pickups: weapons, ammo, health, diamond cases, beds and other usables stop flashing.
//
// From Realism Plus, legacy/realism-plus/features/graphics-no-flashing-items.md, which blanks the
// Mesh_Highlight technique name, and FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482)
// source/gameplay/blinkingitems.ixx, which credits FoxAhead's Far Cry 2 Multi Fixer and hooks the
// highlight pass and the blink marker archetypes.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoBlinkingItems() {
    FCSE::Logf("no blinking pickups: not implemented yet");
}

void __cdecl OnNoBlinkingItemsChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
