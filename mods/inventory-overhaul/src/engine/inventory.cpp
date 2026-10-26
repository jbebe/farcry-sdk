// A pawn's inventory, read at the offsets and called at the addresses of Steam's Dunia.dll.
#include "engine/inventory.h"

#include "engine/memory.h"
#include "fcse_api.h"

namespace InventoryOverhaul::Inventory {

namespace {
    using GetTerminalFn = void*(__cdecl*)();
    using DropWeaponFn = void*(__fastcall*)(void* inventory, void* unused, void* weapon, int keep);
    using UpdateEquipAnimFn = void(__fastcall*)(void* inventory, void* unused);
    using BuildTypeFn = void*(__cdecl*)();
    using HierarchyFn = const void*(__fastcall*)(void* object, void* unused);
    using ItemWeaponFn = void*(__fastcall*)(void* item, void* unused);
    using CategoryFn = const uint32_t*(__fastcall*)(void* weapon, void* unused);

    // The local player's CTerminal, and the inventory's DropWeapon(CWeapon*, bool) and
    // UpdateEquipAnim.
    FCSE::Relocation<GetTerminalFn> g_getTerminal{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<DropWeaponFn> g_dropWeapon{FCSE::Uplay(0x0012DB20)};
    FCSE::Relocation<UpdateEquipAnimFn> g_updateEquipAnim{FCSE::Uplay(0x0012A6F0)};
    // CInventoryItemWeapon's class id, built on first use, and CWeaponPropertiesCommon's class ids.
    FCSE::Relocation<BuildTypeFn> g_buildWeaponItemType{FCSE::Uplay(0x00126210)};
    FCSE::Relocation<const uint32_t*> g_weaponItemType{FCSE::Uplay(0x00FE46EC)};
    FCSE::Relocation<const uint32_t*> g_classes{FCSE::Uplay(0x00F93390)};

    // A component of the player's entity, not its pawn.
    constexpr ptrdiff_t kTerminalPlayer = 0x04;
    constexpr ptrdiff_t kViewInventory = 0x04;
    // The entity's name, an MSVC std::string: its text sits inline until it outgrows 16 bytes.
    constexpr ptrdiff_t kEntityName = 0x14;
    constexpr ptrdiff_t kEntityNameCapacity = 0x28;
    constexpr ptrdiff_t kInventoryItems = 0x04;
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

    void* PlayerEntity() {
        void* terminal = g_getTerminal();
        void* player = terminal != nullptr ? Read<void*>(terminal, kTerminalPlayer) : nullptr;
        return player != nullptr ? Entity(player) : nullptr;
    }

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
}

bool Resolve() {
    return g_getTerminal && g_dropWeapon && g_updateEquipAnim && g_buildWeaponItemType &&
           g_weaponItemType && g_classes;
}

void* OfView(const void* view) { return Read<void*>(view, kViewInventory); }

bool IsPlayers(const void* inventory) {
    const void* owner = Entity(Read<const void*>(inventory, kInventoryOwner));
    return owner != nullptr && owner == PlayerEntity();
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

uint32_t& Id(void* item) {
    return *reinterpret_cast<uint32_t*>(static_cast<uint8_t*>(item) + kItemId);
}

uint32_t InHandId(const void* inventory) { return Read<uint32_t>(inventory, kInventoryInHand); }

bool Drawn(const void* inventory) {
    return Read<uint32_t>(inventory, kInventoryHandState) == kHandStateDrawn;
}

void Equip(void* inventory, uint32_t id) {
    if (Read<uint32_t>(inventory, kInventoryDesired) != id) {
        Write(inventory, kInventoryDesired, id);
        g_updateEquipAnim(inventory, nullptr);
    }
}

void Drop(void* inventory, void* weapon) { g_dropWeapon(inventory, nullptr, weapon, 0); }

}
