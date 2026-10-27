// The wallet is a component of the player's entity, found by its class id as the game's mission
// rewards find it.
#include "engine/economy.h"

#include "engine/inventory.h"
#include "engine/memory.h"
#include "fcse_api.h"

namespace InventoryOverhaul::Economy {

namespace {
    using BuildTypeFn = void*(__cdecl*)();

    // CEconomyComponent's class id, built on first use.
    FCSE::Relocation<BuildTypeFn> g_buildEconomyType{FCSE::Uplay(0x006574F0)};
    FCSE::Relocation<const uint32_t*> g_economyType{FCSE::Uplay(0x016491D8)};

    constexpr ptrdiff_t kDiamondCount = 0x10;
    // The names bought, engine std::strings, and how many; CEconomyComponent::AlreadyBoughtItem
    // searches them.
    constexpr ptrdiff_t kBoughtNames = 0x14;
    constexpr ptrdiff_t kBoughtCount = 0x18;

    const void* Wallet() {
        g_buildEconomyType();
        return Inventory::PlayerComponent(g_economyType.get());
    }
}

bool Resolve() { return g_buildEconomyType && g_economyType; }

int Diamonds() {
    const void* wallet = Wallet();
    return wallet != nullptr ? Read<int32_t>(wallet, kDiamondCount) : -1;
}

std::vector<std::string> Bought() {
    std::vector<std::string> names;
    if (const void* wallet = Wallet()) {
        const auto* strings = Read<const EngineString<char>*>(wallet, kBoughtNames);
        const auto count = Read<uint32_t>(wallet, kBoughtCount);
        for (uint32_t i = 0; i < count; ++i) {
            names.push_back(ReadString(&strings[i]));
        }
    }
    return names;
}

}
