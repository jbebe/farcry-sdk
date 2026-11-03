// Flying: noclip, the engine's own free camera, and the free camera with the game paused.
//
// Modes rather than features, because they are mutually exclusive and share a speed ladder, so the
// exclusion has to live somewhere no option owns. What is here is the mechanism - the engine's camera
// manager, its free camera prototype, the character controller's physics, the game clock - and what
// the options own is the policy: whether noclip and freecam are allowed, and on which key. Pause is
// always on P.
//
// Noclip detaches the player: physics off, the body flown directly, the view still on the head.
// Freecam leaves the player standing and switches the manager to a free camera prototype the shipped
// game still carries but never activates. Neither is reachable from the console.
#pragma once

namespace DevTools::DebugCamera {

// Pause is the free camera with the game clock stopped, on P.
enum class Mode { None, Noclip, Freecam, Pause };

void Install();

// Binds a mode to a virtual key, 0 to unbind, and takes over polling it. Refuses a key another mode
// already holds - one key toggling between two modes would leave no way back to the game - and
// answers which key the mode ended up on, so the caller can say so.
//
// This is where the mutual exclusion lives, next to the Enter/Leave that enforces the rest of it.
int Bind(Mode mode, int key);

}
