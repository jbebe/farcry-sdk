// A pawn's inventory as the engine keeps it (SInventoryViewPawnImpl): its weapon items, the
// guns among them, and the calls that draw and drop them.
#pragma once

#include <cstdint>
#include <vector>

namespace InventoryOverhaul::Inventory {

// A weapon in an inventory: the inventory item and the CWeapon it holds.
struct Weapon {
    void* item;
    void* weapon;
};

// False when an address is missing on this build; nothing below may be called then.
bool Resolve();

// The local player's inventory, or null before the player is spawned.
void* PlayerInventory();
bool IsPlayerEntity(const void* entity);
bool IsPlayers(const void* inventory);
// The entity a component, such as a weapon or a pawn, belongs to.
void* Entity(const void* component);
// The entity an entity proxy, such as the user a pickup is handed, stands for.
void* ProxyEntity(const void* proxy);

// Every weapon but the machete, in the inventory's order.
std::vector<Weapon> Guns(const void* inventory);
bool IsGun(void* weapon);
const uint32_t* Category(void* weapon);
// The archetype the weapon was made from, such as "weapons.Primary.AK47".
const char* Name(void* weapon);

// An item's id, by which the engine selects, draws and saves it.
uint32_t& Id(void* item);
uint32_t InHandId(const void* inventory);
// Whether the gun in hand is out, rather than being drawn or put away.
bool Drawn(const void* inventory);
// Makes the gun whose item has `id` the one being drawn.
void Equip(void* inventory, uint32_t id);
void Drop(void* inventory, void* weapon);

}
