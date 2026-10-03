// The gameplay input pass, as a frame on the game thread in which the player's pawn is in hand.
#pragma once

namespace VehicleOverhaul::PawnTick {

// `look` is the frame's mouse look on the input listener: pitch, then yaw, in half-turns a second.
// Zeroing it keeps the pawn's own look still.
using TickFn = void (*)(void* pawn, float* look);

// Calls `tick` once a frame while a player exists, after any subscribed before it. The first call
// hooks the pass; false, and logged, when it is not found.
bool Subscribe(TickFn tick);

}
