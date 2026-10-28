// The story as the pause menu's Jackal Files and Buddies pages read it, and the game's string table,
// which words it.
#pragma once

#include <string>
#include <vector>

namespace InventoryOverhaul::Story {

// False when an address is missing on this build; everything below is empty then.
bool Install();

// The string `key` of the string table's `section`, such as ("JackalTapes", "A1JT00"), or empty.
// Game thread only, as is everything below.
const std::wstring& Localize(const char* section, const std::string& key);

// The string keys of the Jackal tapes found, in the order the game lists them.
std::vector<std::string> FoundTapes();

// A buddy's status, as CBuddiesManager::GetBuddyStatus counts it.
enum Status { kAlive, kMissing, kDead, kRescueReady, kWounded };

// A buddy met: its name, such as "Frank_Bilders", its status, that status in the pause menu's
// words, and whether it is the player's primary buddy (1), secondary buddy (2) or neither (0).
struct Buddy {
    std::string name;
    Status status;
    std::wstring statusText;
    int active;
};

// The buddies met, most history points first.
std::vector<Buddy> Buddies();

}
