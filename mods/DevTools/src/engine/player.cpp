// The local player, and what hangs off it.
#include "engine/player.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    using GetLocalPlayerFn = void*(__cdecl*)();

    FCSE::Relocation<GetLocalPlayerFn> g_getLocalPlayerSite{FCSE::Uplay(0x00831870)};

    // Resolved once: an unresolved Relocation re-scans on every access, and this is on the frame.
    GetLocalPlayerFn g_getLocalPlayer = nullptr;

    constexpr ptrdiff_t kPlayerScene = 0x04;
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;
}

namespace DevTools::Player {

void Install() {
    if (g_getLocalPlayerSite) {
        g_getLocalPlayer = g_getLocalPlayerSite.get();
        return;
    }
    FCSE::ApiPointer()->Log("player: the local player accessor was not found in this build");
}

void* Local() { return g_getLocalPlayer != nullptr ? g_getLocalPlayer() : nullptr; }

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
