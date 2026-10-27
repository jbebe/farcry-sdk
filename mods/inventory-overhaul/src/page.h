// The notebook's Inventory page, the Magma page build-ui.ps1 adds to the HUD: the guns in hand,
// Items, the belt, and a post-it for the gun in hand.
#pragma once

namespace InventoryOverhaul::Page {

// False when the HUD's Magma calls were not found in this build.
bool Install();

// Writes the page again if what it shows has changed. Game thread, with the player's
// CInventoryViewPawn and inventory, while the notebook is open.
void Update(const void* view, void* inventory);

// Shows the page while the notebook is open and hides it otherwise, a HUD loaded since included.
// Game thread, once a frame.
void Frame();

}
