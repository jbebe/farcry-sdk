// The notebook: a gadget of its own beside the map, out on its own control (Tab), given to the player
// once they carry the map.
#pragma once

namespace InventoryOverhaul::Notebook {

// False when the map or the signals could not be hooked; Tab then does nothing.
bool Install();

// Gives the player the notebook when they carry the map but no notebook. Game thread.
void Frame();

// Whether the notebook is out. Any thread.
bool IsOpen();

}
