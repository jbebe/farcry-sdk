// The player's body as the engine draws it: the headless first-person avatar or the full body.
#pragma once

namespace VehicleOverhaul::ViewMode {

// False, and logged, when this build lacks the view mode switch or the call that discards the full body.
bool Install();

// Draws the player's `pawn` from inside, the avatar, or from outside: its full body, with its shadow and the
// weapons it carries. Game thread only.
void Set(void* pawn, bool firstPerson);

}
