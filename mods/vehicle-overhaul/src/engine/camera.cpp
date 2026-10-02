// The local player's camera manager, and the cameras it can be asked for by archetype name.
//
// See docs/docs/engine-internals/free-camera-and-noclip.md.
#include "engine/camera.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr ptrdiff_t kPlayerScene = 0x04;
    // The manager is embedded in the scene rather than pointed at.
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;
    constexpr ptrdiff_t kManagerLocked = 0x14;

    using LocalPlayerFn = void*(__cdecl*)();
    using ActivateByNameFn = void(__fastcall*)(void* manager, void* unused, const char* name,
                                               int32_t notify);
    using ActiveCameraFn = void*(__fastcall*)(void* manager);

    FCSE::Relocation<LocalPlayerFn> g_localPlayer{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<ActivateByNameFn> g_activateByName{FCSE::Uplay(0x0057CDE0)};
    FCSE::Relocation<ActiveCameraFn> g_activeCamera{FCSE::Uplay(0x0057C1B0)};
}

namespace VehicleOverhaul::Camera {

bool Install() {
    if (!g_localPlayer || !g_activateByName || !g_activeCamera) {
        FCSE::ApiPointer()->Log("camera: the camera manager's entry points were not found in this build");
        return false;
    }
    return true;
}

void* Manager() {
    auto* local = static_cast<uint8_t*>(g_localPlayer());
    auto* scene = local != nullptr ? *reinterpret_cast<uint8_t**>(local + kPlayerScene) : nullptr;
    return scene != nullptr ? scene + kSceneCameraManager : nullptr;
}

void* Active(void* manager) { return manager != nullptr ? g_activeCamera(manager) : nullptr; }

void ActivateByName(void* manager, const char* name) { g_activateByName(manager, nullptr, name, 1); }

bool Locked(void* manager) { return *(static_cast<uint8_t*>(manager) + kManagerLocked) != 0; }

}
