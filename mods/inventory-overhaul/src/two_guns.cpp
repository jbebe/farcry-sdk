// Each gun keeps its slot on it as entity data. The engine keeps its classes: only the number keys,
// the gun count and what a pickup replaces change.
#include "two_guns.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <utility>
#include <vector>

namespace InventoryOverhaul::TwoGuns {

namespace {
    using ActionFn = void(__fastcall*)(void* node, void* unused, void* context, void* extra);
    using AddWeaponFn = void(__fastcall*)(void* inventory, void* unused, void* weapon);
    using SelectWeaponFn = bool(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using FindByClassFn = void*(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using PrePickupFn = void(__fastcall*)(void* pickup, void* unused, void* pawn);
    using PickFn = bool(__fastcall*)(void* pickup, void* unused, void* user, uint32_t flag);

    // A state machine node's action; the inventory's AddWeapon(CWeapon*), SelectWeapon(class) and
    // GetWeaponFromCategory(class); CPickupWeapon's PrePickup(pawn) and Pick(user, flag).
    FCSE::Relocation<ActionFn> g_actionSite{FCSE::Uplay(0x00CE27F0)};
    FCSE::Relocation<AddWeaponFn> g_addWeaponSite{FCSE::Uplay(0x001285F0)};
    FCSE::Relocation<SelectWeaponFn> g_selectWeaponSite{FCSE::Uplay(0x0012D670)};
    FCSE::Relocation<FindByClassFn> g_findByClassSite{FCSE::Uplay(0x00128FB0)};
    FCSE::Relocation<PrePickupFn> g_prePickupSite{FCSE::Uplay(0x00099450)};
    FCSE::Relocation<PickFn> g_pickSite{FCSE::Uplay(0x00099E90)};
    ActionFn g_action = nullptr;
    AddWeaponFn g_addWeapon = nullptr;
    SelectWeaponFn g_selectWeapon = nullptr;
    FindByClassFn g_findByClass = nullptr;
    PrePickupFn g_prePickup = nullptr;
    PickFn g_pick = nullptr;

    constexpr int kSlots = 2;
    constexpr const char* kSlotKey = "InventoryOverhaul.Slot";
    // The node's action. Number keys 3, 2 and 4 run the actions that select the primary, secondary
    // and special class.
    constexpr ptrdiff_t kActionType = 0x10;
    constexpr uint32_t kSelectPrimary = 0x0A;
    constexpr uint32_t kSelectSecondary = 0x0B;
    constexpr uint32_t kSelectSpecial = 0x0C;
    constexpr uint32_t kTwinSalt = FCSE::Crc32("InventoryOverhaul.Twin");

    // The slot the number key being handled asks for, 0 for none; -1 outside a number key.
    int g_keySlot = -1;
    // Set while anything picks a weapon up.
    bool g_inPickup = false;
    // The gun the player's pickup added, and the inventory it went into.
    struct Picked {
        void* inventory;
        void* weapon;
    };
    Picked g_picked{};
    int g_lastSlot = 1;

    int SlotOf(void* weapon) {
        int32_t slot = 0;
        return FCSE::ApiPointer()->EntityData->GetInt(Inventory::Entity(weapon), kSlotKey, &slot)
                   ? slot
                   : 0;
    }

    void SetSlot(void* weapon, int slot) {
        FCSE::ApiPointer()->EntityData->SetInt(Inventory::Entity(weapon), kSlotKey, slot);
    }

    const Inventory::Weapon* InSlot(const std::vector<Inventory::Weapon>& guns, int slot) {
        for (const Inventory::Weapon& gun : guns) {
            if (SlotOf(gun.weapon) == slot) {
                return &gun;
            }
        }
        return nullptr;
    }

    // The gun in hand, else the one last drawn with a key, else the first of `guns`, never empty.
    const Inventory::Weapon& ToReplace(const void* inventory,
                                       const std::vector<Inventory::Weapon>& guns) {
        for (const Inventory::Weapon& gun : guns) {
            if (Inventory::Id(gun.item) == Inventory::InHandId(inventory)) {
                return gun;
            }
        }
        const Inventory::Weapon* last = InSlot(guns, g_lastSlot);
        return last != nullptr ? *last : guns.front();
    }

    // Gives every gun a slot of its own: the slots already held are kept, and the other guns, then
    // `added`, take the free ones. A third gun, from a save made without the mod, gets none.
    void SettleSlots(const std::vector<Inventory::Weapon>& guns, void* added) {
        bool taken[kSlots + 1] = {};
        std::vector<void*> unplaced;
        for (const Inventory::Weapon& gun : guns) {
            if (gun.weapon == added) {
                continue;
            }
            const int slot = SlotOf(gun.weapon);
            if (slot >= 1 && slot <= kSlots && !taken[slot]) {
                taken[slot] = true;
            } else {
                unplaced.push_back(gun.weapon);
            }
        }
        unplaced.push_back(added);
        int next = 1;
        for (void* weapon : unplaced) {
            while (next <= kSlots && taken[next]) {
                ++next;
            }
            SetSlot(weapon, next <= kSlots ? next++ : 0);
        }
    }

    // Two guns of the same model share an item id, which would make the engine draw the first
    // whichever is asked for; the later one gets an id of its own.
    void SeparateTwins(const std::vector<Inventory::Weapon>& guns) {
        for (auto gun = guns.begin(); gun != guns.end(); ++gun) {
            uint32_t& id = Inventory::Id(gun->item);
            while (std::any_of(guns.begin(), gun, [&](const Inventory::Weapon& earlier) {
                return Inventory::Id(earlier.item) == id;
            })) {
                id += kTwinSalt;
            }
        }
    }

    // Draws `gun`. The engine draws the first gun of the class asked for, so `gun` then takes its
    // place, which matters when both guns are of one class.
    bool Draw(void* inventory, const Inventory::Weapon& gun) {
        if (!g_selectWeapon(inventory, nullptr, Inventory::Category(gun.weapon))) {
            return false;
        }
        Inventory::Equip(inventory, Inventory::Id(gun.item));
        return true;
    }

    void __fastcall OnAction(void* node, void* unused, void* context, void* extra) {
        const auto action = Read<uint32_t>(node, kActionType);
        g_keySlot = action == kSelectSecondary ? 1
                    : action == kSelectPrimary ? 2
                    : action == kSelectSpecial ? 0
                                               : -1;
        g_action(node, unused, context, extra);
        g_keySlot = -1;
    }

    // Key 2 draws gun 1 and key 3 gun 2, whatever their class; key 4 draws nothing.
    bool __fastcall OnSelectWeapon(void* inventory, void* unused, const uint32_t* category) {
        const int slot = g_keySlot;
        if (slot < 0 || !Inventory::IsPlayers(inventory)) {
            return g_selectWeapon(inventory, unused, category);
        }
        if (slot == 0) {
            return false;
        }
        const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        const Inventory::Weapon* gun = InSlot(guns, slot);
        if (gun == nullptr || (Inventory::Id(gun->item) == Inventory::InHandId(inventory) &&
                               Inventory::Drawn(inventory))) {
            return false;
        }
        if (!Draw(inventory, *gun)) {
            return false;
        }
        g_lastSlot = slot;
        FCSE::Logf("key: gun %d, the %s", slot, Inventory::Name(gun->weapon));
        return true;
    }

    // A gun picked up with two carried takes the place of the one in hand. Loading a save adds its
    // guns here too, and drops none.
    void __fastcall OnAddWeapon(void* inventory, void* unused, void* weapon) {
        if (!Inventory::IsPlayers(inventory) || !Inventory::IsGun(weapon)) {
            return g_addWeapon(inventory, unused, weapon);
        }
        if (g_inPickup) {
            const std::vector<Inventory::Weapon> carried = Inventory::Guns(inventory);
            if (carried.size() >= kSlots) {
                const Inventory::Weapon& out = ToReplace(inventory, carried);
                FCSE::Logf("the %s takes a slot: the %s is dropped", Inventory::Name(weapon),
                           Inventory::Name(out.weapon));
                Inventory::Drop(inventory, out.weapon);
            }
        }
        g_addWeapon(inventory, unused, weapon);
        const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        SettleSlots(guns, weapon);
        SeparateTwins(guns);
        if (g_inPickup) {
            g_picked = {inventory, weapon};
        }
        FCSE::Logf("the %s is gun %d", Inventory::Name(weapon), SlotOf(weapon));
    }

    // A pickup replaces the carried gun of its class, which the player's no longer is: AddWeapon
    // makes room instead.
    void* __fastcall OnFindByClass(void* inventory, void* unused, const uint32_t* category) {
        return g_inPickup && Inventory::IsPlayers(inventory)
                   ? nullptr
                   : g_findByClass(inventory, unused, category);
    }

    void __fastcall OnPrePickup(void* pickup, void* unused, void* pawn) {
        g_inPickup = true;
        g_prePickup(pickup, unused, pawn);
        g_inPickup = false;
    }

    // The gun the player picks up is drawn, as it is in the game.
    bool __fastcall OnPick(void* pickup, void* unused, void* user, uint32_t flag) {
        g_inPickup = true;
        const bool picked = g_pick(pickup, unused, user, flag);
        g_inPickup = false;
        const Picked added = std::exchange(g_picked, {});
        if (added.weapon == nullptr) {
            return picked;
        }
        for (const Inventory::Weapon& gun : Inventory::Guns(added.inventory)) {
            if (gun.weapon == added.weapon) {
                Draw(added.inventory, gun);
            }
        }
        return picked;
    }
}

bool Install() {
    if (!Inventory::Resolve()) {
        FCSE::ApiPointer()->Log("an inventory address is missing on this build: two guns is off");
        return false;
    }
    const bool added = Hook(g_addWeaponSite, &OnAddWeapon, &g_addWeapon);
    const bool keys = Hook(g_selectWeaponSite, &OnSelectWeapon, &g_selectWeapon) &&
                      Hook(g_actionSite, &OnAction, &g_action);
    const bool pickups = Hook(g_findByClassSite, &OnFindByClass, &g_findByClass) &&
                         Hook(g_prePickupSite, &OnPrePickup, &g_prePickup) &&
                         Hook(g_pickSite, &OnPick, &g_pick);
    FCSE::Logf("two guns: adding %s, keys %s, pickups %s", added ? "on" : "off",
               keys ? "on" : "off", pickups ? "on" : "off");
    return added || keys || pickups;
}

}
