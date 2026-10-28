// Items: what the player carries outside the two gun slots, kept on the player's entity so that it
// is saved with the game.
#pragma once

#include <string>
#include <vector>

namespace InventoryOverhaul::Items {

// A gun is kept as the archetype it was made from, its wear and its loaded rounds; below zero, a
// wear or round count leaves the gun as the game makes it. Anything else is an id and a count.
struct Record {
    std::string id;
    bool gun = false;
    float reliability = -1.0f;
    int loaded = -1;
    int count = 1;
};

// Items holds at most this many guns, so four in all with the two slots.
constexpr size_t kMostGuns = 2;

// The player's Items, empty when there is no player. Everything here runs on the game thread.
std::vector<Record> Load();
void Save(const std::vector<Record>& items);
size_t GunCount(const std::vector<Record>& items);

// Moves gun 1 or 2 into Items. False when there is no such gun, or Items holds two guns already.
bool Stow(void* inventory, int slot);
// Puts the gun in row `index` in hand; with two guns carried, the one in hand takes its row.
bool Take(void* inventory, size_t index);
// Adds a new gun when `id` names a weapon archetype, else one more of the item `id`.
bool Give(const std::string& id);
void Discard(size_t index);
// Drops row `index`: a stowed gun falls at the pawn's feet as it was stowed, and one of an item is
// thrown away. False when there is no such row or its gun could not be made.
bool Drop(void* inventory, size_t index);

}
