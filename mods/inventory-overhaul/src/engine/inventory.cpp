// A pawn's inventory, read at the offsets and called at the addresses of Steam's Dunia.dll.
#include "engine/inventory.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstring>

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
    FCSE::Relocation<ReliabilityFn> g_reliability{FCSE::Uplay(0x0012E9F0)};
    FCSE::Relocation<RoundsFn> g_ammoInClip{FCSE::Uplay(0x0012E950)};
    FCSE::Relocation<RoundsFn> g_rememberedInClip{FCSE::Uplay(0x0012EB50)};
    FCSE::Relocation<SetLoadedFn> g_setLoaded{FCSE::Uplay(0x0012F850)};
    // CInventoryViewPawn's count of the rounds of one ammo type outside the magazines.
    FCSE::Relocation<SpareRoundsFn> g_spareRounds{FCSE::Uplay(0x00126000)};
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
    // The entity's name, an MSVC std::string: its text sits inline until it outgrows 16 bytes.
    constexpr ptrdiff_t kEntityName = 0x14;
    constexpr ptrdiff_t kEntityNameCapacity = 0x28;
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

    bool IsWeaponItem(void* item, uint32_t weaponItemType) {
        const void* hierarchy = Method<HierarchyFn>(item, kHierarchySlot)(item, nullptr);
        const auto count = Read<uint32_t>(hierarchy, kHierarchyCount);
        for (uint32_t i = 0; i < count; ++i) {
            if (Read<uint32_t>(hierarchy, kHierarchyIds + i * sizeof(uint32_t)) == weaponItemType) {
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
}

bool Install() {
    return g_getTerminal && g_dropWeapon && g_addByName && g_selectAtOnce && g_updateEquipAnim &&
           g_reliability && g_ammoInClip && g_rememberedInClip && g_setLoaded && g_spareRounds && g_buildWeaponItemType && g_weaponItemType && g_classes &&
           Hook(g_canSpawnPickupSite, &OnCanSpawnPickup, &g_canSpawnPickup);
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
    return Read<uint32_t>(entity, kEntityNameCapacity) < 16
               ? reinterpret_cast<const char*>(entity + kEntityName)
               : Read<const char*>(entity, kEntityName);
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

uint32_t& Id(void* item) {
    return *reinterpret_cast<uint32_t*>(static_cast<uint8_t*>(item) + kItemId);
}

uint32_t InHandId(const void* inventory) { return Read<uint32_t>(inventory, kInventoryInHand); }

bool Drawn(const void* inventory) {
    return Read<uint32_t>(inventory, kInventoryHandState) == kHandStateDrawn;
}

bool HandEmpty(const void* inventory) { return EquippedEntity(inventory) == nullptr; }

void SelectAtOnce(void* inventory, const uint32_t* category) {
    g_selectAtOnce(inventory, nullptr, category);
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
