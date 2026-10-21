// The campaign state every feature reads: the difficulty, and which entity is the local player.
#include "engine/game.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    using GetServiceFn = void*(__fastcall*)(void* manager, void* unused, const void* id, int create);
    using GetTerminalFn = void*(__cdecl*)();

    // CGameModeManager's instance and GetGameModeService, then CFCXGameplayManager's service id
    // and the count that stays 0 until that id is built.
    FCSE::Relocation<void**> g_gameModeManager{FCSE::Uplay(0x01644D74)};
    FCSE::Relocation<GetServiceFn> g_getService{FCSE::Uplay(0x004DBB80)};
    FCSE::Relocation<const void*> g_gameplayManagerId{FCSE::Uplay(0x0164C33C)};
    FCSE::Relocation<const uint32_t*> g_gameplayManagerIdBuilt{FCSE::Uplay(0x0164C328)};
    // The local player's CTerminal.
    FCSE::Relocation<GetTerminalFn> g_getTerminal{FCSE::Uplay(0x00831870)};

    constexpr ptrdiff_t kGameplayManagerDifficulty = 0x40;
    constexpr ptrdiff_t kTerminalPlayer = 0x04;
    constexpr ptrdiff_t kPlayerProxy = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;

    const char* const kNames[] = {"Easy", "Normal", "Hardcore", "Infamous"};
}

namespace DifficultyOverhaul::Game {

Difficulty CurrentDifficulty() {
    if (!g_gameModeManager || !g_getService || !g_gameplayManagerId || !g_gameplayManagerIdBuilt) {
        return kUnknown;
    }
    void* manager = *g_gameModeManager.get();
    if (manager == nullptr || *g_gameplayManagerIdBuilt.get() == 0) {
        return kUnknown;
    }
    void* gameplay = g_getService(manager, nullptr, g_gameplayManagerId, 1);
    if (gameplay == nullptr) {
        return kUnknown;
    }
    const auto level = Read<uint32_t>(gameplay, kGameplayManagerDifficulty);
    return level <= kInfamous ? static_cast<Difficulty>(level) : kUnknown;
}

const char* Name(Difficulty difficulty) {
    return difficulty == kUnknown ? "difficulty unknown" : kNames[difficulty];
}

void* PlayerEntity() {
    void* terminal = g_getTerminal ? g_getTerminal() : nullptr;
    if (terminal == nullptr) {
        return nullptr;
    }
    auto* player = Read<void*>(terminal, kTerminalPlayer);
    return player != nullptr ? ProxyEntity(Read<void*>(player, kPlayerProxy)) : nullptr;
}

void* ProxyEntity(const void* proxy) {
    return proxy != nullptr ? Read<void*>(proxy, kProxyEntity) : nullptr;
}

}
