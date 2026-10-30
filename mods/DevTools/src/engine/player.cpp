// The local player, and what hangs off it.
#include "engine/player.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    using GetLocalPlayerFn = void*(__cdecl*)();
    using PawnClassFn = const uint8_t*(__cdecl*)();

    FCSE::Relocation<GetLocalPlayerFn> g_getLocalPlayerSite{FCSE::Uplay(0x00831870)};

    // CPawn's class hierarchy, built on first use, and where CPawn's own class id sits in it.
    FCSE::Relocation<PawnClassFn> g_pawnClass{FCSE::Uplay(0x00040A80)};
    constexpr ptrdiff_t kHierarchyPawnId = 0x14;

    // Resolved once: an unresolved Relocation re-scans on every access, and this is on the frame.
    GetLocalPlayerFn g_getLocalPlayer = nullptr;

    constexpr ptrdiff_t kPlayerScene = 0x04;
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;
}

namespace DevTools::Player {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (g_getLocalPlayerSite) {
        g_getLocalPlayer = g_getLocalPlayerSite.get();
    } else {
        api->Log("player: the local player accessor was not found in this build");
    }
    if (!g_pawnClass) {
        api->Log("player: the pawn's class was not found in this build");
    }
}

void* Local() { return g_getLocalPlayer != nullptr ? g_getLocalPlayer() : nullptr; }

void* Pawn() {
    void* local = Local();
    if (local == nullptr || !g_pawnClass) {
        return nullptr;
    }

    // The scene is a component of the player's entity, which also holds the pawn.
    void* entity = FCSE::ApiPointer()->EntityData->EntityOf(Read<void*>(local, kPlayerScene));
    return entity != nullptr ? Entity::Component(entity, g_pawnClass() + kHierarchyPawnId) : nullptr;
}

void* CameraManager(void* local) {
    if (local == nullptr) {
        return nullptr;
    }

    // The manager is embedded in the scene rather than pointed at, so this is its address.
    auto* scene = *reinterpret_cast<uint8_t**>(static_cast<uint8_t*>(local) + kPlayerScene);
    return scene != nullptr ? scene + kSceneCameraManager : nullptr;
}

void* CameraManager() { return CameraManager(Local()); }

bool ObjectIsLive(void* object, void* vtable, void* local) {
    if (object == nullptr || vtable == nullptr || local == nullptr) {
        return false;
    }
    return *reinterpret_cast<void**>(object) == vtable;
}

}
