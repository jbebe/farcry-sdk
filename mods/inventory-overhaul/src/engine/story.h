// The story as the pause menu's Jackal Files and Buddies pages read it, and the game's string table,
// which words it.
#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace InventoryOverhaul::Story {

// False when an address is missing on this build; everything below is empty then.
bool Install();

// The string `key` of the string table's `section`, such as ("JackalTapes", "A1JT00"), or empty.
// Game thread only, as is everything below.
const std::wstring& Localize(const char* section, const std::string& key);

// The string `key` of `section` with the names it leaves as words, such as PRIMARY_BUDDY_NAME,
// filled in as the pause menu fills them; empty when this build lacks the calls.
std::wstring Words(const char* section, const std::string& key);

// The string keys of the Jackal then Predecessor tapes found, in the game's order.
std::vector<std::string> FoundTapes();

// Plays tape `key` from its start as the pause menu does, stopping any other, while the game runs on;
// false when it did not start, as while scripted music plays. With a `sound`, the tape is another
// mod's cassette, that sound event, played and stopped as the Jackal tapes are, clicks included.
bool PlayTape(const std::string& key, uint32_t sound = 0);
void StopTape();
// The key of the tape PlayTape started while it still plays, else empty.
std::string PlayingTape();
// Holds a cassette's bank and the clicks loaded, for PlayTape: a play loads nothing.
void HoldCassette(uint32_t sound);
// Starts a cassette once its click has played; every frame.
void Frame();

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
