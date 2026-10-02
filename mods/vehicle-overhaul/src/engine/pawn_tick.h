// The gameplay input pass, as a frame on the game thread in which the player's pawn is in hand.
#pragma once

namespace VehicleOverhaul::PawnTick {

using TickFn = void (*)(void* pawn);

// Calls `tick` once a frame while a player exists. False, and logged, when the pass is not found.
bool Install(TickFn tick);

}
