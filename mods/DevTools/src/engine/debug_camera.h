// Flying: noclip, and the engine's own free camera.
//
// Two modes rather than two features, because they are mutually exclusive and share a speed ladder,
// so the exclusion has to live somewhere neither option owns. What is here is the mechanism - the
// engine's camera manager, its free camera prototype, the character controller's physics - and what
// the options own is the policy: whether a mode is allowed, and on which key.
//
// Noclip detaches the player: physics off, the body flown directly, the view still on the head.
// Freecam leaves the player standing and switches the manager to a free camera prototype the shipped
// game still carries but never activates. Neither is reachable from the console.
#pragma once

namespace DevTools::DebugCamera {

enum class Mode { None, Noclip, Freecam };

void Install();

// Binds a mode to a virtual key, 0 to unbind, and takes over polling it. Refuses a key another mode
// already holds - one key toggling between two modes would leave no way back to the game - and
// answers which key the mode ended up on, so the caller can say so.
//
// This is where the mutual exclusion lives, next to the Enter/Leave that enforces the rest of it.
int Bind(Mode mode, int key);

}
