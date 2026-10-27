// The player's wallet, CEconomyComponent: the diamonds, and what was bought from the shop.
#pragma once

#include <string>
#include <vector>

namespace InventoryOverhaul::Economy {

// False when an address is missing on this build.
bool Resolve();

// The diamonds the player has, or -1 when there is no player. Game thread only.
int Diamonds();

// The names of what the player has bought, as the shop names its items: "ak47 repair manual",
// "assault webbing", "medium first aid manual". Game thread only.
std::vector<std::string> Bought();

}
