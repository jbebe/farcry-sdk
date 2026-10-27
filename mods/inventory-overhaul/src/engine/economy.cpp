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

    // An engine std::string: an allocator word, the text inline until it outgrows 16 bytes, its
    // length and its capacity.
    constexpr ptrdiff_t kStringText = 0x04;
    constexpr ptrdiff_t kStringLength = 0x14;
    constexpr ptrdiff_t kStringCapacity = 0x18;
    constexpr ptrdiff_t kStringSize = 0x1C;
    constexpr uint32_t kInlineCapacity = 16;

    const void* Wallet() {
        g_buildEconomyType();
        return Inventory::PlayerComponent(g_economyType.get());
    }

    std::string Text(const uint8_t* string) {
        const char* text = Read<uint32_t>(string, kStringCapacity) < kInlineCapacity
                               ? reinterpret_cast<const char*>(string + kStringText)
                               : Read<const char*>(string, kStringText);
        return std::string(text, Read<uint32_t>(string, kStringLength));
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
        const auto* strings = Read<const uint8_t*>(wallet, kBoughtNames);
        const auto count = Read<uint32_t>(wallet, kBoughtCount);
        for (uint32_t i = 0; i < count; ++i) {
            names.push_back(Text(strings + i * kStringSize));
        }
    }
    return names;
}

}
