#include "engine/difficulty.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>
#include <iterator>

namespace {
    using GetServiceFn = void*(__fastcall*)(void* manager, void* unused, const void* id, int create);

    // CGameModeManager's instance and GetGameModeService, then CFCXGameplayManager's service id
    // and the count that stays 0 until that id is built.
    FCSE::Relocation<void**> g_gameModeManager{FCSE::Uplay(0x01644D74)};
    FCSE::Relocation<GetServiceFn> g_getService{FCSE::Uplay(0x004DBB80)};
    FCSE::Relocation<const void*> g_gameplayManagerId{FCSE::Uplay(0x0164C33C)};
    FCSE::Relocation<const uint32_t*> g_gameplayManagerIdBuilt{FCSE::Uplay(0x0164C328)};

    constexpr ptrdiff_t kGameplayManagerDifficulty = 0x40;

    const char* const kNames[] = {"Easy", "Normal", "Hardcore", "Infamous"};
    constexpr char kUnknown[] = "difficulty unknown";
}

namespace DevTools::Difficulty {

const char* Name() {
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
    return level < std::size(kNames) ? kNames[level] : kUnknown;
}

}
