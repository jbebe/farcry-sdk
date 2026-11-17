// The game's own slide of the arms behind a turn, which can be taken away so the plugin moves them
// instead. See docs/docs/engine-internals/first-person-aiming.md#the-arms-behind-a-turn.
#pragma once

namespace AimingOverhaul::TurnSlide {

// False, and logged, when the slide cannot be hooked; the game's then stays.
bool Install();

// Whether the game's slide is taken away, from the next frame.
void SetTaken(bool taken);

}
