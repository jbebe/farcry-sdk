// A pawn's inventory as the engine keeps it (SInventoryViewPawnImpl): its weapon items, the
// guns among them, and the calls that draw, drop, make and take away guns.
#pragma once

#include <cstdint>
#include <vector>

namespace InventoryOverhaul::Inventory {

// A weapon in an inventory: the inventory item and the CWeapon it holds.
struct Weapon {
    void* item;
    void* weapon;
};

// The weapon classes, in the order of a weapon's selCategory.
enum Class { kMachete, kPrimary, kSecondary, kSpecial };

// False when an address is missing on this build or a hook was refused; nothing below may be
// called then.
bool Install();

// The inventory behind a pawn's CInventoryViewPawn.
void* OfView(const void* view);
// Whether an inventory is the local player's.
bool IsPlayers(const void* inventory);
// The local player's entity, or null when there is none.
void* PlayerEntity();
// The entity a component, such as a weapon or a pawn, belongs to.
void* Entity(const void* component);

// Every weapon but the machete, in the inventory's order.
std::vector<Weapon> Guns(const void* inventory);
bool Is(void* weapon, Class weaponClass);
bool IsGun(void* weapon);
const uint32_t* Category(void* weapon);
// The archetype the weapon was made from, such as "weapons.Primary.AK47".
const char* Name(void* weapon);
// How worn a gun is, as the engine counts it, and the rounds it has loaded.
float Reliability(void* weapon);
void SetReliability(void* weapon, float reliability);
int Loaded(void* weapon);
void SetLoaded(void* weapon, int rounds);

// An item's id, by which the engine selects, draws and saves it.
uint32_t& Id(void* item);
uint32_t InHandId(const void* inventory);
// Whether the gun in hand is out, rather than being drawn or put away.
bool Drawn(const void* inventory);
// Makes the gun whose item has `id` the one being drawn.
void Equip(void* inventory, uint32_t id);
// Drops a gun at the pawn's feet, or with Remove takes it away and leaves nothing behind.
void Drop(void* inventory, void* weapon);
void Remove(void* inventory, void* weapon);
// Makes a new gun of `archetype` and adds it, even when one of that model is carried. False when
// none was made.
bool AddByName(void* inventory, const char* archetype);

}
