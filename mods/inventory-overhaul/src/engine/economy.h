// The player's wallet, CEconomyComponent.
#pragma once

namespace InventoryOverhaul::Economy {

// False when an address is missing on this build.
bool Resolve();

// The diamonds the player has, or -1 when there is no player. Game thread only.
int Diamonds();

}
