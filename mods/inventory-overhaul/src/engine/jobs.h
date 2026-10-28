// The jobs taken, as the pause menu's main page reads them from the map's objective markers.
#pragma once

#include <string>

namespace InventoryOverhaul::Jobs {

// False when an address is missing on this build; Find finds nothing then.
bool Install();

// The current job in the game's words, buddies' and lieutenants' names filled in: its line, up to two
// objectives, and the Underground's objective; each empty when there is none.
struct Current {
    std::wstring job;
    std::wstring objectives[2];
    std::wstring underground;
};

// Game thread only.
Current Find();

}
