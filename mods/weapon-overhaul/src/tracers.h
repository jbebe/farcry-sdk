// Which shots leave a tracer: only those of the M249, the PKM, the Dragunov and the AS50, the
// player's own first-person shots among them.
#pragma once

namespace WeaponOverhaul::Tracers {

// Hooks where a shot decides on its tracer. False, and logged, when that place is not found.
bool Install();

// Whether the choice above is made, rather than the engine's own: a tracer every few rounds from
// most guns, and never from the player's.
void SetEnabled(bool enabled);

}
