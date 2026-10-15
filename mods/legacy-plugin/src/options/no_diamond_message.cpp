// Hide diamond message: a diamond pickup no longer queues the engine's own "diamonds" HUD message.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/economy-briefcase-tracker.md (its Dunia.dll
// half; the scripted message that replaces it is data). The patch jumps PushDiamondPicked past the
// queue call.
//
// Not implemented yet.
#include "fcse_api.h"

void InstallNoDiamondMessage() {
    FCSE::Logf("hide diamond message: not implemented yet");
}

void __cdecl OnNoDiamondMessageChanged(const FCSE_SettingValue* /*value*/, void* /*userdata*/) {}
