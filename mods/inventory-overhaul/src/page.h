// The notebook's Inventory page, the Magma page build-ui.ps1 adds to the HUD: the guns in hand,
// Items, the belt, and a post-it for the row selected.
#pragma once

namespace InventoryOverhaul::Page {

// False when the HUD's Magma calls were not found in this build.
bool Install();

// Writes the page again if what it shows has changed. Game thread, with the player's
// CInventoryViewPawn and inventory, while the notebook is open.
void Update(const void* view, void* inventory);

// Moves the selection `steps` rows down, or up when negative, the next time the page is written.
// Any thread.
void Scroll(int steps);

// Shows the page while the notebook is open and hides it otherwise, a HUD loaded since included.
// Game thread, once a frame.
void Frame();

}
