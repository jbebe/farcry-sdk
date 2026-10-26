// A window in DevTools' overlay for trying Items before the notebook exists: the guns in hand,
// Items, and buttons to stow, take, give and discard.
#pragma once

namespace InventoryOverhaul::TestWindow {

// Adds the window, when DevTools is installed. From FCSE_OnRegisterFunctions on.
void Add();

// Whether the window is waiting on the game: a button was pressed, or it is open and showing what
// is carried.
bool Pending();

// Does what the window asked for, with the player's inventory, on the game thread.
void Run(void* inventory);

}
