// Items: what the player carries outside the two gun slots, kept on the player's entity so that it
// is saved with the game.
#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace InventoryOverhaul::Items {

// A gun is kept as the archetype it was made from, its wear and its loaded rounds; below zero, a
// wear or round count leaves the gun as the game makes it. Anything else is an id and a count, a
// cassette its sound event, and an item the description its pickup gave it, in UTF-8.
struct Record {
    std::string id;
    bool gun = false;
    float reliability = -1.0f;
    int loaded = -1;
    int count = 1;
    uint32_t sound = 0;
    std::string description;
};

// Items holds at most this many guns, so four in all with the two slots.
constexpr size_t kMostGuns = 2;

// The player's Items, empty when there is no player. Everything here runs on the game thread.
std::vector<Record> Load();
void Save(const std::vector<Record>& items);
size_t GunCount(const std::vector<Record>& items);

// Moves gun 1 or 2 into Items. False when there is no such gun, or Items holds two guns already.
bool Stow(void* inventory, int slot);
// Puts the gun in row `index` in gun `slot`, 1 or 2; the gun there takes its row, and if that one was
// in hand, the new one is drawn. While the notebook is out, no gun is in hand.
bool Take(void* inventory, size_t index, int slot);
// Adds a new gun when `id` names a weapon archetype, else one more of the item `id`, with its `sound`
// and `description`.
bool Give(const std::string& id, uint32_t sound = 0, const std::string& description = {});
void Discard(size_t index);
// Drops row `index`: a stowed gun falls at the pawn's feet as it was stowed, or is only removed when
// it cannot be made, and one of an item is thrown away. False when there is no such row.
bool Drop(void* inventory, size_t index);

}
