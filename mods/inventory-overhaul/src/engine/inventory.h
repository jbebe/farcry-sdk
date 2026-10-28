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
// called then. Hooks once however often it is called.
bool Install();

// A CPawn's CInventoryViewPawn, and the inventory behind one.
void* ViewOf(const void* pawn);
void* OfView(const void* view);
// Whether an inventory is the local player's.
bool IsPlayers(const void* inventory);
// The local player's entity, or null when there is none.
void* PlayerEntity();
// The entity a component, such as a weapon or a pawn, belongs to.
void* Entity(const void* component);
// The player entity's component of the class `type`, or null.
void* PlayerComponent(const uint32_t* type);

// An entity's component by class id, or null.
const void* Component(void* entity, const uint32_t* type);
// The local player's CPawn, or null when there is none.
void* PlayerPawn();
// How many items an inventory holds.
size_t Count(const void* inventory);

// Every weapon but the machete, in the inventory's order.
std::vector<Weapon> Guns(const void* inventory);
bool Is(void* weapon, Class weaponClass);
bool IsGun(void* weapon);
const uint32_t* Category(void* weapon);
// The archetype the weapon was made from, such as "weapons.Primary.AK47".
const char* Name(void* weapon);
// How worn a gun is, as the engine counts it.
float Reliability(void* weapon);
void SetReliability(void* weapon, float reliability);
// The rounds in a gun's magazine: for the gun in hand its own, for a holstered one those it will
// load when next drawn. SetLoaded sets the latter.
int Loaded(const void* inventory, void* weapon);
void SetLoaded(void* weapon, int rounds);
// The rounds of a gun's ammo the pawn carries outside every magazine, by its CInventoryViewPawn.
int SpareRounds(const void* view, void* weapon);
// The rounds of the ammo type named `ammo` the pawn carries, such as "syringe".
int Ammo(const void* view, const char* ammo);
// The uses left of the throwable of `category`, "grenade" or "molotov", or 0 without one.
int Throwables(const void* view, const char* category);
// The pawn's gadget of a category, such as Crc32("map"), by its CInventoryViewPawn, or null.
void* Gadget(const void* view, uint32_t category);
// Makes a gadget of `archetype` and adds it, or adds `uses` to the one of that archetype carried.
// Null when none was made.
void* AddGadget(void* inventory, const char* archetype, int uses);
// The malaria pills the player carries, or -1 when there is no player.
int Pills();
// The player's reputation level, 0 to 5, or -1 when there is no player.
int Reputation();

// An item's id, by which the engine selects, draws and saves it.
uint32_t& Id(void* item);
uint32_t InHandId(const void* inventory);
// Whether the gun in hand is out, rather than being drawn or put away.
bool Drawn(const void* inventory);
// Whether nothing is in hand, as after the gun in hand was dropped.
bool HandEmpty(const void* inventory);
// Draws the first gun of a class at once, as a pickup does, rather than switching to it.
void SelectAtOnce(void* inventory, const uint32_t* category);
// Makes the gun whose item has `id` the one being drawn.
void Equip(void* inventory, uint32_t id);
// Drops a gun at the pawn's feet, or with Remove takes it away and leaves nothing behind.
void Drop(void* inventory, void* weapon);
void Remove(void* inventory, void* weapon);
// Makes a new gun of `archetype` and adds it, even when one of that model is carried. False when
// none was made.
bool AddByName(void* inventory, const char* archetype);

}
