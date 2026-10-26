// The notebook: the map held in hand with its other face up, out on its own control (Tab). Tab and
// the map's key each bring it out with their face up, turn it to their face while it is out, and
// put it away when their face is already up.
#pragma once

namespace InventoryOverhaul::Notebook {

// False when the map or the signals could not be hooked; the map then works as in the game.
bool Install();

// Whether the map is out with the notebook's face up. Any thread.
bool IsOpen();

}
