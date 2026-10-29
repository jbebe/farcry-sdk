// Each gun keeps its slot on it as entity data. The engine keeps its classes: only the number keys,
// the gun count and what a pickup replaces change.
#include "two_guns.h"

#include "engine/economy.h"
#include "engine/game_thread.h"
#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"
#include "items.h"
#include "test_window.h"

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
    using EquippedFn = void*(__fastcall*)(void* view, void* unused);

    // CInventoryViewPawn::GetEquippedWeapon, which the HUD asks for the player every frame; a state
    // machine node's action; the inventory's AddWeapon(CWeapon*), SelectWeapon(class) and
    // GetWeaponFromCategory(class); CPickupWeapon's PrePickup(pawn) and Pick(user, flag).
    FCSE::Relocation<EquippedFn> g_equippedSite{FCSE::Uplay(0x00127DA0)};
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
    EquippedFn g_equipped = nullptr;

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
    // Set when a weapon is added some other way, as a save's are, until the player's guns are seen.
    bool g_check = true;
    // The gun the player's pickup added, and the inventory it went into.
    struct Picked {
        void* inventory;
        void* weapon;
    };
    Picked g_picked{};
    int g_lastSlot = 1;
}

void SetSlot(void* weapon, int slot) {
    FCSE::ApiPointer()->EntityData->SetInt(Inventory::Entity(weapon), kSlotKey, slot);
}

int SlotOf(void* weapon) {
    int32_t slot = 0;
    return FCSE::ApiPointer()->EntityData->GetInt(Inventory::Entity(weapon), kSlotKey, &slot) ? slot
                                                                                              : 0;
}

const Inventory::Weapon* InSlot(const std::vector<Inventory::Weapon>& guns, int slot) {
    for (const Inventory::Weapon& gun : guns) {
        if (SlotOf(gun.weapon) == slot) {
            return &gun;
        }
    }
    return nullptr;
}

const Inventory::Weapon* InHand(const void* inventory, const std::vector<Inventory::Weapon>& guns) {
    for (const Inventory::Weapon& gun : guns) {
        if (Inventory::Id(gun.item) == Inventory::InHandId(inventory)) {
            return &gun;
        }
    }
    return nullptr;
}

const Inventory::Weapon& ToReplace(const void* inventory,
                                   const std::vector<Inventory::Weapon>& guns) {
    if (const Inventory::Weapon* gun = InHand(inventory, guns)) {
        return *gun;
    }
    const Inventory::Weapon* last = InSlot(guns, g_lastSlot);
    return last != nullptr ? *last : guns.front();
}

// The engine draws the first gun of the class asked for, so `gun` then takes its place, which
// matters when both guns are of one class. With nothing in hand a switch brings nothing up, so the
// gun is drawn at once, as a pickup draws it.
bool Draw(void* inventory, const Inventory::Weapon& gun) {
    if (Inventory::HandEmpty(inventory)) {
        Inventory::SelectAtOnce(inventory, Inventory::Category(gun.weapon));
    } else if (!g_selectWeapon(inventory, nullptr, Inventory::Category(gun.weapon))) {
        return false;
    }
    Inventory::Equip(inventory, Inventory::Id(gun.item));
    return true;
}

namespace {
    // The slot whose key the gun's class had in the game: the secondary's 2 is gun 1, the
    // primary's 3 gun 2. The special class's 4 reaches nothing now.
    int GameSlot(void* weapon) {
        return Inventory::Is(weapon, Inventory::kSecondary) ? 1
               : Inventory::Is(weapon, Inventory::kPrimary) ? 2
                                                            : 0;
    }

    // Gives every gun a slot of its own. Slots already held are kept; a gun without one, as every
    // gun is in a save from before the mod, takes its class's slot from the game if free, else the
    // free one. With `drop`, a gun left over is dropped, and true is returned if it was in hand.
    bool PlaceGuns(void* inventory, const std::vector<Inventory::Weapon>& guns, bool drop) {
        bool taken[kSlots + 1] = {};
        std::vector<const Inventory::Weapon*> unplaced;
        for (const Inventory::Weapon& gun : guns) {
            const int slot = SlotOf(gun.weapon);
            if (slot >= 1 && slot <= kSlots && !taken[slot]) {
                taken[slot] = true;
            } else {
                unplaced.push_back(&gun);
            }
        }
        std::stable_partition(unplaced.begin(), unplaced.end(), [](const Inventory::Weapon* gun) {
            return GameSlot(gun->weapon) != 0;
        });
        bool droppedInHand = false;
        for (const Inventory::Weapon* gun : unplaced) {
            int slot = GameSlot(gun->weapon);
            if (slot == 0 || taken[slot]) {
                slot = static_cast<int>(std::find(taken + 1, taken + kSlots + 1, false) - taken);
            }
            if (slot <= kSlots) {
                taken[slot] = true;
                SetSlot(gun->weapon, slot);
                FCSE::Logf("the %s is gun %d", Inventory::Name(gun->weapon), slot);
            } else if (drop) {
                FCSE::Logf("the %s is a third gun: dropped", Inventory::Name(gun->weapon));
                droppedInHand |= Inventory::Id(gun->item) == Inventory::InHandId(inventory);
                Inventory::Drop(inventory, gun->weapon);
            } else {
                SetSlot(gun->weapon, 0);
                FCSE::Logf("the %s is a third gun", Inventory::Name(gun->weapon));
            }
        }
        return droppedInHand;
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

    // Draws gun 1, else gun 2.
    void DrawFirst(void* inventory) {
        const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        const Inventory::Weapon* gun = InSlot(guns, 1);
        if (gun == nullptr) {
            gun = InSlot(guns, 2);
        }
        if (gun != nullptr) {
            Draw(inventory, *gun);
        }
    }

    // Places the player's guns and drops a third; gun 1 is drawn if the dropped one was in hand.
    void Tidy(void* inventory) {
        std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        SeparateTwins(guns);
        if (PlaceGuns(inventory, guns, true)) {
            DrawFirst(inventory);
        }
    }

    // A save's guns are added before the save's own data on them comes back, which replaces any
    // slot given then; they are placed here, the first time the HUD asks for the player's gun. The
    // test window's buttons are carried out here too.
    void* __fastcall OnEquipped(void* view, void* unused) {
        static bool running = false;
        if ((g_check || TestWindow::Pending()) && !g_inPickup && !running &&
            GameThread::IsCurrent()) {
            void* inventory = Inventory::OfView(view);
            if (Inventory::IsPlayers(inventory)) {
                running = true;
                if (std::exchange(g_check, false)) {
                    Tidy(inventory);
                }
                TestWindow::Run(inventory);
                running = false;
            }
        }
        return g_equipped(view, unused);
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
        std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        const Inventory::Weapon* gun = InSlot(guns, slot);
        if (gun == nullptr && !guns.empty()) {
            Tidy(inventory);
            guns = Inventory::Guns(inventory);
            gun = InSlot(guns, slot);
        }
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

    // A gun the player picks up with two carried, or with as many as Items allows in all, takes the
    // place of the one in hand; with none carried it falls back at their feet. A weapon added any
    // other way is placed the next time the HUD asks for the player's gun.
    void __fastcall OnAddWeapon(void* inventory, void* unused, void* weapon) {
        if (!g_inPickup || !Inventory::IsPlayers(inventory) || !Inventory::IsGun(weapon)) {
            g_check |= !g_inPickup;
            return g_addWeapon(inventory, unused, weapon);
        }
        const std::vector<Inventory::Weapon> carried = Inventory::Guns(inventory);
        const bool full = carried.size() + Items::GunCount(Items::Load()) >= Items::kMostGuns;
        if (!carried.empty() && (carried.size() >= kSlots || full)) {
            const Inventory::Weapon& out = ToReplace(inventory, carried);
            FCSE::Logf("the %s takes a slot: the %s is dropped", Inventory::Name(weapon),
                       Inventory::Name(out.weapon));
            Inventory::Drop(inventory, out.weapon);
        }
        g_addWeapon(inventory, unused, weapon);
        if (carried.empty() && full) {
            FCSE::Logf("the %s would be a gun too many: dropped", Inventory::Name(weapon));
            Inventory::Drop(inventory, weapon);
            return;
        }
        const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
        SeparateTwins(guns);
        PlaceGuns(inventory, guns, false);
        g_picked = {inventory, weapon};
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

void Settle(void* inventory) {
    const std::vector<Inventory::Weapon> guns = Inventory::Guns(inventory);
    SeparateTwins(guns);
    PlaceGuns(inventory, guns, false);
}

void Release(void* inventory, const Inventory::Weapon& gun, bool drop) {
    const bool inHand = Inventory::Id(gun.item) == Inventory::InHandId(inventory);
    if (drop) {
        FCSE::Logf("dropped gun %d, the %s", SlotOf(gun.weapon), Inventory::Name(gun.weapon));
        Inventory::Drop(inventory, gun.weapon);
    } else {
        Inventory::Remove(inventory, gun.weapon);
    }
    if (inHand) {
        DrawFirst(inventory);
    }
}

bool Install() {
    if (!Inventory::Install() || !Economy::Resolve() || !GameThread::Install()) {
        FCSE::ApiPointer()->Log("an inventory address is missing on this build: two guns is off");
        return false;
    }
    const bool added = Hook(g_addWeaponSite, &OnAddWeapon, &g_addWeapon) &&
                       Hook(g_equippedSite, &OnEquipped, &g_equipped);
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
