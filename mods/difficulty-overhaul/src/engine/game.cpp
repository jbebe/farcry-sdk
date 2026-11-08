// The campaign state every feature reads: the difficulty, the local player, entities and the game clock.
#include "engine/game.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {
    using GetServiceFn = void*(__fastcall*)(void* manager, void* unused, const void* id, int create);
    using GetTerminalFn = void*(__cdecl*)();
    using GetOwnerFn = void*(__fastcall*)(uint8_t* owner, void* unused, uint8_t** proxy);
    using DestroyProxyFn = void(__fastcall*)(uint8_t* proxy, void* unused);

    // CGameModeManager's instance and GetGameModeService, then CFCXGameplayManager's service id
    // and the count that stays 0 until that id is built.
    FCSE::Relocation<void**> g_gameModeManager{FCSE::Uplay(0x01644D74)};
    FCSE::Relocation<GetServiceFn> g_getService{FCSE::Uplay(0x004DBB80)};
    FCSE::Relocation<const void*> g_gameplayManagerId{FCSE::Uplay(0x0164C33C)};
    FCSE::Relocation<const uint32_t*> g_gameplayManagerIdBuilt{FCSE::Uplay(0x0164C328)};
    // The local player's CTerminal, and the game clock.
    FCSE::Relocation<GetTerminalFn> g_getTerminal{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<void**> g_clock{FCSE::Uplay(0x01606360)};
    // CPhysOwner::GetOwnerEntity, which hands back a CEntityProxy holding a reference, and the proxy's
    // destructor.
    FCSE::Relocation<GetOwnerFn> g_getOwner{FCSE::Uplay(0x0048ED40)};
    FCSE::Relocation<DestroyProxyFn> g_destroyProxy{FCSE::Uplay(0x0029B7F0)};

    constexpr ptrdiff_t kClockSeconds = 0x30;
    constexpr ptrdiff_t kGameplayManagerDifficulty = 0x40;
    constexpr ptrdiff_t kTerminalPlayer = 0x04;
    constexpr ptrdiff_t kPlayerProxy = 0x08;
    constexpr ptrdiff_t kProxyReferences = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;
    constexpr ptrdiff_t kBodyOwner = 0x08;
    constexpr ptrdiff_t kEntityId = 0x08;
    constexpr ptrdiff_t kEntityPosition = 0x60;
    constexpr ptrdiff_t kEntityForward = 0x40;

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

bool IsPlayerEntity(const void* entity) { return entity != nullptr && entity == PlayerEntity(); }

void* ProxyEntity(const void* proxy) {
    return proxy != nullptr ? Read<void*>(proxy, kProxyEntity) : nullptr;
}

void* EntityOf(void* component) { return FCSE::ApiPointer()->EntityData->EntityOf(component); }

void* OwnerOf(uint8_t* body) {
    uint8_t* proxy = nullptr;
    if (body == nullptr || !g_getOwner || !g_destroyProxy || !EngineFree) {
        return nullptr;
    }
    g_getOwner(body + kBodyOwner, nullptr, &proxy);
    void* entity = ProxyEntity(proxy);
    if (proxy != nullptr && --*reinterpret_cast<int*>(proxy + kProxyReferences) == 0) {
        g_destroyProxy(proxy, nullptr);
        EngineFree(proxy);
    }
    return entity;
}

uint64_t FullId(const void* entity) {
    return entity != nullptr ? Read<uint64_t>(entity, kEntityId) : 0;
}

uint32_t Id(const void* entity) { return static_cast<uint32_t>(FullId(entity)); }

void Position(const void* entity, float position[3]) {
    std::copy_n(Vec3(entity, kEntityPosition), 3, position);
}

void Forward(const void* entity, float forward[3]) {
    std::copy_n(Vec3(entity, kEntityForward), 3, forward);
}

float Distance(const float a[3], const float b[3]) {
    float squared = 0.0f;
    for (int axis = 0; axis < 3; ++axis) {
        squared += (a[axis] - b[axis]) * (a[axis] - b[axis]);
    }
    return std::sqrt(squared);
}

bool ClockFound() { return static_cast<bool>(g_clock); }

double Now() { return Read<double>(*g_clock.get(), kClockSeconds); }

}
