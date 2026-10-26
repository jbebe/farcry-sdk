// The wallet is a component of the player's entity, found by its class id as the game's mission
// rewards find it.
#include "engine/economy.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"

namespace InventoryOverhaul::Economy {

namespace {
    using GetComponentFn = void*(__fastcall*)(void* entity, void* unused, const uint32_t* type);
    using BuildTypeFn = void*(__cdecl*)();

    // An entity's component by class id, and CEconomyComponent's class id, built on first use.
    FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};
    FCSE::Relocation<BuildTypeFn> g_buildEconomyType{FCSE::Uplay(0x006574F0)};
    FCSE::Relocation<const uint32_t*> g_economyType{FCSE::Uplay(0x016491D8)};

    constexpr ptrdiff_t kDiamondCount = 0x10;
}

bool Resolve() { return g_getComponent && g_buildEconomyType && g_economyType; }

int Diamonds() {
    void* player = Inventory::PlayerEntity();
    if (player == nullptr) {
        return -1;
    }
    g_buildEconomyType();
    const void* wallet = g_getComponent(player, nullptr, g_economyType.get());
    return wallet != nullptr ? Read<int32_t>(wallet, kDiamondCount) : -1;
}

}
