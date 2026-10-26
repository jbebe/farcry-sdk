// The notebook's Inventory page: the guns in hand, Items, and a post-it for the gun in hand.
#pragma once

namespace InventoryOverhaul::Page {

// Draws the page again if what it shows has changed. Game thread, with the player's
// CInventoryViewPawn and inventory, while the notebook is open.
void Update(const void* view, void* inventory);

}
