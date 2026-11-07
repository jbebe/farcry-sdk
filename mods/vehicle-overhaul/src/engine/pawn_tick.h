// The gameplay input pass, as a frame on the game thread in which the player's pawn is in hand.
#pragma once

namespace VehicleOverhaul::PawnTick {

using TickFn = void (*)(void* pawn);

// Calls `tick` once a frame while a player exists, after any subscribed before it. The first call
// hooks the pass; false, and logged, when it is not found.
bool Subscribe(TickFn tick);

}
