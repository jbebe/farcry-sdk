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
enum Status { kAlive, kMissing, kMercyKilled, kRescueReady, kWounded };

// A buddy met: its name, such as "Frank_Bilders", its status, and that status in the pause menu's
// words.
struct Buddy {
    std::string name;
    Status status;
    std::wstring statusText;
};

// The buddies met, most history points first.
std::vector<Buddy> Buddies();

}
