// A pawn's inventory, read at the offsets and called at the addresses of Steam's Dunia.dll.
#include "engine/inventory.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstring>
#include <string_view>
#include <utility>

namespace InventoryOverhaul::Inventory {

namespace {
    using GetTerminalFn = void*(__cdecl*)();
    using DropWeaponFn = void*(__fastcall*)(void* inventory, void* unused, void* weapon, int keep);
    using CanSpawnPickupFn = bool(__fastcall*)(void* inventory, void* unused);
    using AddByNameFn = bool(__fastcall*)(void* inventory, void* unused, const char* archetype);
    using UpdateEquipAnimFn = void(__fastcall*)(void* inventory, void* unused);
    using BuildTypeFn = void*(__cdecl*)();
    using HierarchyFn = const void*(__fastcall*)(void* object, void* unused);
    using ItemWeaponFn = void*(__fastcall*)(void* item, void* unused);
    using CategoryFn = const uint32_t*(__fastcall*)(void* weapon, void* unused);
    using SelectAtOnceFn = void(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using ReliabilityFn = float(__fastcall*)(void* weapon, void* unused);
    using SetReliabilityFn = void(__fastcall*)(void* weapon, void* unused, float reliability);
    using RoundsFn = int(__fastcall*)(void* weapon, void* unused);
    using SetLoadedFn = void(__fastcall*)(void* weapon, void* unused, int rounds);
    using SpareRoundsFn = int(__fastcall*)(const void* view, void* unused, const uint32_t* ammoType);

    // The local player's CTerminal; the inventory's DropWeapon(CWeapon*, bool), the CanSpawnPickup
    // question a drop asks first, AddWeaponByName(archetype), the immediate select a pickup draws
    // with, and UpdateEquipAnim; CWeapon's GetReliability, its AmmoInClip and RememberedAmmoInClip,
    // and the setter of RememberedAmmoInClip and RememberedAmmoOverflow.
    FCSE::Relocation<GetTerminalFn> g_getTerminal{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<DropWeaponFn> g_dropWeapon{FCSE::Uplay(0x0012DB20)};
    FCSE::Relocation<CanSpawnPickupFn> g_canSpawnPickupSite{FCSE::Uplay(0x0012D950)};
    FCSE::Relocation<AddByNameFn> g_addByName{FCSE::Uplay(0x0012B540)};
    FCSE::Relocation<SelectAtOnceFn> g_selectAtOnce{FCSE::Uplay(0x0012B9B0)};
    FCSE::Relocation<UpdateEquipAnimFn> g_updateEquipAnim{FCSE::Uplay(0x0012A6F0)};
    FCSE::Relocation<UpdateEquipAnimFn> g_putAwayAtOnce{FCSE::Uplay(0x0012AF80)};
    FCSE::Relocation<ReliabilityFn> g_reliability{FCSE::Uplay(0x0012E9F0)};
    FCSE::Relocation<RoundsFn> g_ammoInClip{FCSE::Uplay(0x0012E950)};
    FCSE::Relocation<RoundsFn> g_rememberedInClip{FCSE::Uplay(0x0012EB50)};
    FCSE::Relocation<SetLoadedFn> g_setLoaded{FCSE::Uplay(0x0012F850)};
    // CInventoryViewPawn's count of the rounds of one ammo type outside the magazines.
    FCSE::Relocation<SpareRoundsFn> g_spareRounds{FCSE::Uplay(0x00126000)};
    // CInventoryViewPawn::GetGadgetFromCategory, CGadget::GetUses, and the inventory's
    // AddGadget(archetype, uses).
    using GadgetFn = void*(__fastcall*)(const void* view, void* unused, const uint32_t* category);
    using UsesFn = int(__fastcall*)(void* gadget, void* unused);
    using AddGadgetFn = void*(__fastcall*)(void* inventory, void* unused, const char* archetype,
                                           int uses);
    FCSE::Relocation<GadgetFn> g_gadgetFromCategory{FCSE::Uplay(0x00129DF0)};
    FCSE::Relocation<UsesFn> g_uses{FCSE::Uplay(0x001496C0)};
    FCSE::Relocation<AddGadgetFn> g_addGadget{FCSE::Uplay(0x0012B790)};
    // CPawn's class hierarchy, built on first use, whose fourth class id is CPawn's own.
    using ClassFn = const void*(__cdecl*)();
    FCSE::Relocation<ClassFn> g_pawnClass{FCSE::Uplay(0x00040A80)};
    constexpr size_t kPawnClass = 3;
    // An entity's component by class id; CFCXCountersComponentPlayerSP's class id, built when its
    // name is still unset, its RemainingPills, and its reputation level, 0 to 5.
    using GetComponentFn = void*(__fastcall*)(void* entity, void* unused, const uint32_t* type);
    FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};
    FCSE::Relocation<BuildClassIdFn> g_buildCountersType{FCSE::Uplay(0x00657F30)};
    FCSE::Relocation<const void* const*> g_countersTypeName{FCSE::Uplay(0x01649818)};
    FCSE::Relocation<const uint32_t*> g_countersType{FCSE::Uplay(0x01649838)};
    constexpr ptrdiff_t kRemainingPills = 0xF0;
    constexpr ptrdiff_t kReputation = 0x184;
    constexpr int kTopReputation = 5;
    // The wallet's class id, CEconomyComponent's, built on first use; its diamonds, and the names
    // bought, engine std::strings, and how many, which CEconomyComponent::AlreadyBoughtItem searches.
    FCSE::Relocation<BuildTypeFn> g_buildEconomyType{FCSE::Uplay(0x006574F0)};
    FCSE::Relocation<const uint32_t*> g_economyType{FCSE::Uplay(0x016491D8)};
    constexpr ptrdiff_t kDiamondCount = 0x10;
    constexpr ptrdiff_t kBoughtNames = 0x14;
    constexpr ptrdiff_t kBoughtCount = 0x18;
    // CInventoryItemWeapon's class id, built on first use, and CWeaponPropertiesCommon's class ids.
    FCSE::Relocation<BuildTypeFn> g_buildWeaponItemType{FCSE::Uplay(0x00126210)};
    FCSE::Relocation<const uint32_t*> g_weaponItemType{FCSE::Uplay(0x00FE46EC)};
    FCSE::Relocation<const uint32_t*> g_classes{FCSE::Uplay(0x00F93390)};
    CanSpawnPickupFn g_canSpawnPickup = nullptr;

    // A component of the player's entity, not its pawn.
    constexpr ptrdiff_t kTerminalPlayer = 0x04;
    // The pawn's data, which holds its inventory view, whose second field is the inventory.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataInventoryView = 0x4F0;
    constexpr ptrdiff_t kViewInventory = 0x04;
    // The entity's name, an engine std::string.
    constexpr ptrdiff_t kEntityName = 0x10;
    constexpr ptrdiff_t kInventoryItems = 0x04;
    // The equipped weapon's entity proxy, which holds the entity at +0xC.
    constexpr ptrdiff_t kInventoryEquipped = 0x10;
    constexpr ptrdiff_t kProxyEntity = 0x0C;
    constexpr ptrdiff_t kInventoryInHand = 0x14;
    constexpr ptrdiff_t kInventoryDesired = 0x18;
    constexpr ptrdiff_t kInventoryOwner = 0x48;
    constexpr ptrdiff_t kInventoryHandState = 0x74;
    constexpr uint32_t kHandStateDrawn = 1;
    constexpr ptrdiff_t kItemId = 0x0C;
    constexpr ptrdiff_t kHierarchyCount = 0x04;
    constexpr ptrdiff_t kHierarchyIds = 0x08;
    constexpr size_t kHierarchySlot = 1;
    constexpr size_t kItemWeaponSlot = 0x14 / sizeof(void*);
    constexpr size_t kWeaponCategorySlot = 0x8C / sizeof(void*);
    constexpr size_t kSetReliabilitySlot = 0xBC / sizeof(void*);
    // The weapon's state object, its CWeaponPropertiesCommon, and the ammo type in that.
    constexpr ptrdiff_t kWeaponState = 0x24;
    constexpr ptrdiff_t kStateProperties = 0x04;
    constexpr ptrdiff_t kPropertiesAmmoType = 0x88;

    // Set while a gun is taken away, so its drop leaves no pickup.
    bool g_removing = false;

    uint32_t ClassIdAt(const void* hierarchy, size_t index) {
        return Read<uint32_t>(hierarchy, kHierarchyIds + index * sizeof(uint32_t));
    }

    bool IsWeaponItem(void* item, uint32_t weaponItemType) {
        const void* hierarchy = Method<HierarchyFn>(item, kHierarchySlot)(item, nullptr);
        const auto count = Read<uint32_t>(hierarchy, kHierarchyCount);
        for (uint32_t i = 0; i < count; ++i) {
            if (ClassIdAt(hierarchy, i) == weaponItemType) {
                return true;
            }
        }
        return false;
    }

    bool __fastcall OnCanSpawnPickup(void* inventory, void* unused) {
        return !g_removing && g_canSpawnPickup(inventory, unused);
    }

    const void* EquippedEntity(const void* inventory) {
        const void* proxy = Read<const void*>(inventory, kInventoryEquipped);
        return proxy != nullptr ? Read<const void*>(proxy, kProxyEntity) : nullptr;
    }

    const void* Counters() {
        return PlayerComponent(ClassId(g_buildCountersType, g_countersTypeName, g_countersType));
    }

    // The wallet is found by its class id as the game's mission rewards find it.
    const void* Wallet() {
        g_buildEconomyType();
        return PlayerComponent(g_economyType.get());
    }
}

bool Install() {
    return g_getTerminal && g_dropWeapon && g_addByName && g_selectAtOnce && g_updateEquipAnim && g_putAwayAtOnce &&
           g_reliability && g_ammoInClip && g_rememberedInClip && g_setLoaded && g_spareRounds &&
           g_gadgetFromCategory && g_uses && g_addGadget && g_pawnClass && g_getComponent &&
           g_buildCountersType && g_countersTypeName && g_countersType && g_buildEconomyType &&
           g_economyType && g_buildWeaponItemType && g_weaponItemType && g_classes &&
           (g_canSpawnPickup != nullptr ||
            Hook(g_canSpawnPickupSite, &OnCanSpawnPickup, &g_canSpawnPickup));
}

void* ViewOf(const void* pawn) { return Read<uint8_t*>(pawn, kPawnData) + kDataInventoryView; }

void* OfView(const void* view) { return Read<void*>(view, kViewInventory); }

bool IsPlayers(const void* inventory) {
    const void* owner = Entity(Read<const void*>(inventory, kInventoryOwner));
    return owner != nullptr && owner == PlayerEntity();
}

void* PlayerEntity() {
    void* terminal = g_getTerminal();
    void* player = terminal != nullptr ? Read<void*>(terminal, kTerminalPlayer) : nullptr;
    return player != nullptr ? Entity(player) : nullptr;
}

void* Entity(const void* component) {
    return FCSE::ApiPointer()->EntityData->EntityOf(const_cast<void*>(component));
}

const void* Component(void* entity, const uint32_t* type) { return g_getComponent(entity, nullptr, type); }

void* PlayerComponent(const uint32_t* type) {
    void* player = PlayerEntity();
    return player != nullptr ? g_getComponent(player, nullptr, type) : nullptr;
}

void* PlayerPawn() {
    const uint32_t pawn = ClassIdAt(g_pawnClass(), kPawnClass);
    return PlayerComponent(&pawn);
}

size_t Count(const void* inventory) {
    return Read<uint32_t>(Read<const void*>(inventory, kInventoryItems), sizeof(void*));
}

std::vector<Weapon> Guns(const void* inventory) {
    g_buildWeaponItemType();
    const uint32_t weaponItemType = *g_weaponItemType.get();
    std::vector<Weapon> guns;
    const void* list = Read<const void*>(inventory, kInventoryItems);
    void* const* items = Read<void* const*>(list, 0);
    const auto count = Read<uint32_t>(list, sizeof(void*));
    for (uint32_t i = 0; i < count; ++i) {
        if (!IsWeaponItem(items[i], weaponItemType)) {
            continue;
        }
        void* weapon = Method<ItemWeaponFn>(items[i], kItemWeaponSlot)(items[i], nullptr);
        if (weapon != nullptr && IsGun(weapon)) {
            guns.push_back({items[i], weapon});
        }
    }
    return guns;
}

const uint32_t* Category(void* weapon) {
    return Method<CategoryFn>(weapon, kWeaponCategorySlot)(weapon, nullptr);
}

bool Is(void* weapon, Class weaponClass) {
    return *Category(weapon) == g_classes.get()[weaponClass];
}

bool IsGun(void* weapon) { return !Is(weapon, kMachete); }

const char* Name(void* weapon) {
    const auto* entity = static_cast<const uint8_t*>(Entity(weapon));
    if (entity == nullptr) {
        return "?";
    }
    return reinterpret_cast<const EngineString<char>*>(entity + kEntityName)->Text();
}

float Reliability(void* weapon) { return g_reliability(weapon, nullptr); }

void SetReliability(void* weapon, float reliability) {
    Method<SetReliabilityFn>(weapon, kSetReliabilitySlot)(weapon, nullptr, reliability);
}

int Loaded(const void* inventory, void* weapon) {
    return EquippedEntity(inventory) == Entity(weapon) ? g_ammoInClip(weapon, nullptr)
                                                        : g_rememberedInClip(weapon, nullptr);
}

void SetLoaded(void* weapon, int rounds) { g_setLoaded(weapon, nullptr, rounds); }

int SpareRounds(const void* view, void* weapon) {
    const auto* properties = Read<const uint8_t*>(Read<const void*>(weapon, kWeaponState),
                                                  kStateProperties);
    return g_spareRounds(view, nullptr,
                         reinterpret_cast<const uint32_t*>(properties + kPropertiesAmmoType));
}

int Ammo(const void* view, const char* ammo) {
    const uint32_t type = FCSE::Crc32(ammo);
    return g_spareRounds(view, nullptr, &type);
}

int Throwables(const void* view, const char* category) {
    void* gadget = Gadget(view, FCSE::Crc32(category));
    return gadget != nullptr ? g_uses(gadget, nullptr) : 0;
}

void* Gadget(const void* view, uint32_t category) {
    return g_gadgetFromCategory(view, nullptr, &category);
}

void* AddGadget(void* inventory, const char* archetype, int uses) {
    return g_addGadget(inventory, nullptr, archetype, uses);
}

int Pills() {
    const void* counters = Counters();
    return counters != nullptr ? Read<int32_t>(counters, kRemainingPills) : -1;
}

int Reputation() {
    const void* counters = Counters();
    return counters != nullptr ? std::min<int>(Read<uint8_t>(counters, kReputation), kTopReputation) : -1;
}

int Diamonds() {
    const void* wallet = Wallet();
    return wallet != nullptr ? Read<int32_t>(wallet, kDiamondCount) : -1;
}

bool IsBought(const std::string& name) {
    const void* wallet = Wallet();
    if (wallet == nullptr) {
        return false;
    }
    const auto* bought = Read<const EngineString<char>*>(wallet, kBoughtNames);
    const auto count = Read<uint32_t>(wallet, kBoughtCount);
    return std::any_of(bought, bought + count, [&](const EngineString<char>& each) {
        return std::string_view(each.Text(), each.length) == name;
    });
}

uint32_t& Id(void* item) {
    return *reinterpret_cast<uint32_t*>(static_cast<uint8_t*>(item) + kItemId);
}

bool IsInHand(const void* inventory, void* item) { return Id(item) == Read<uint32_t>(inventory, kInventoryInHand); }

bool Drawn(const void* inventory) {
    return Read<uint32_t>(inventory, kInventoryHandState) == kHandStateDrawn;
}

bool HandEmpty(const void* inventory) { return EquippedEntity(inventory) == nullptr; }

void SelectAtOnce(void* inventory, const Weapon& gun) {
    // The engine does nothing with a gun of the class in hand, as dropping one leaves the other there,
    // and draws the first gun of the class, so the hand is emptied and this one goes first for the call.
    g_putAwayAtOnce(inventory, nullptr);
    void* list = Read<void*>(inventory, kInventoryItems);
    void** items = Read<void**>(list, 0);
    void** place = std::find(items, items + Read<uint32_t>(list, sizeof(void*)), gun.item);
    std::swap(*items, *place);
    g_selectAtOnce(inventory, nullptr, Category(gun.weapon));
    std::swap(*items, *place);
}

void Equip(void* inventory, uint32_t id) {
    if (Read<uint32_t>(inventory, kInventoryDesired) != id) {
        Write(inventory, kInventoryDesired, id);
        g_updateEquipAnim(inventory, nullptr);
    }
}

void Drop(void* inventory, void* weapon) { g_dropWeapon(inventory, nullptr, weapon, 0); }

void Remove(void* inventory, void* weapon) {
    g_removing = true;
    Drop(inventory, weapon);
    g_removing = false;
}

bool AddByName(void* inventory, const char* archetype) {
    // The engine adds no gun whose item id, made from its archetype, is already carried, so those
    // ids are turned aside for the call.
    std::vector<uint32_t*> sameModel;
    for (const Weapon& gun : Guns(inventory)) {
        if (std::strcmp(Name(gun.weapon), archetype) == 0) {
            sameModel.push_back(&Id(gun.item));
        }
    }
    for (uint32_t* id : sameModel) {
        *id = ~*id;
    }
    const bool added = g_addByName(inventory, nullptr, archetype);
    for (uint32_t* id : sameModel) {
        *id = ~*id;
    }
    return added;
}

}
