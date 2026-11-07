// Left mouse held over the map reads it close: the view zooms in as down iron sights, the look slowing
// with it, and the sheet stays where it was as the look turns, so the mouse looks over it; let go, the view
// zooms out and the sheet swings back in front. The map's own use of left mouse, showing and hiding its
// icons, is taken while it is out.
#pragma once

#include <cstdint>

namespace MapOverhaul::Read {

bool Install();

// Once a frame on the game thread, with the pawn holding the map, or null while none does: zooms as left
// mouse says and returns how far the look has turned since the zoom came in, eased back to nought once let
// go, in radians, left positive; the sheet is to be turned back by as much.
float Update(uint8_t* pawn, float seconds);

}
