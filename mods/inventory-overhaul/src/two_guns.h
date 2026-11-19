// Two guns, any class: gun 1 on key 3, gun 2 on key 2, the wheel through the machete and both, and
// a gun picked up going into an empty slot, gun 1 first, or with both carried replacing the one in
// hand, else gun 1, which the HUD shows.
#pragma once

#include "engine/inventory.h"

#include <vector>

namespace InventoryOverhaul::TwoGuns {

// False when nothing could be hooked.
bool Install();

// The gun in slot 1 or 2, or null.
const Inventory::Weapon* InSlot(const std::vector<Inventory::Weapon>& guns, int slot);
// The gun in hand, or null.
const Inventory::Weapon* InHand(const void* inventory, const std::vector<Inventory::Weapon>& guns);
int SlotOf(void* weapon);
// Puts a gun in slot 1 or 2, or in none with 0; Settle keeps it there.
void SetSlot(void* weapon, int slot);
bool Draw(void* inventory, const Inventory::Weapon& gun);
// Gives a gun added outside a pickup its slot, and an item id of its own.
void Settle(void* inventory);
// Takes a carried gun out of the inventory, dropped at the pawn's feet or gone; if it was in hand,
// gun 1 is drawn, else gun 2.
void Release(void* inventory, const Inventory::Weapon& gun, bool drop);

// While it lives, a gun added outside a pickup is not marked to be placed: the plugin's own adds
// place or drop their gun themselves, and the engine asks for the gun in hand mid-drop.
struct Quiet {
    Quiet();
    ~Quiet();
    Quiet(const Quiet&) = delete;
    Quiet& operator=(const Quiet&) = delete;
};

}
