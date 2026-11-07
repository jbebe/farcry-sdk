// The camera the local player sees through: the camera manager's active camera, embedded in the
// local player's scene.
#include "engine/camera.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>

namespace {
    using ThirdPerson::Field;

    constexpr ptrdiff_t kPlayerScene = 0x04;
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;
    // Set on the manager while a cutscene owns the camera.
    constexpr ptrdiff_t kManagerLocked = 0x14;

    using LocalPlayerFn = uint8_t*(__cdecl*)();
    using ActiveCameraFn = uint8_t*(__fastcall*)(uint8_t* manager);
    using ActivateByNameFn = void(__fastcall*)(uint8_t* manager, void* unused, const char* name,
                                               int32_t notify);
    FCSE::Relocation<LocalPlayerFn> g_localPlayer{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<ActiveCameraFn> g_activeCamera{FCSE::Uplay(0x0057C1B0)};
    FCSE::Relocation<ActivateByNameFn> g_activateByName{FCSE::Uplay(0x0057CDE0)};

    // The local player's camera manager, or null outside a session.
    uint8_t* Manager() {
        uint8_t* local = g_localPlayer && g_activeCamera ? g_localPlayer() : nullptr;
        uint8_t* scene = local != nullptr ? Field<uint8_t*>(local, kPlayerScene) : nullptr;
        return scene != nullptr ? scene + kSceneCameraManager : nullptr;
    }
}

bool ThirdPerson::Camera::Install() {
    if (!g_localPlayer || !g_activeCamera || !g_activateByName) {
        FCSE::ApiPointer()->Log("camera: the camera manager's entry points were not found");
        return false;
    }
    return true;
}

uint8_t* ThirdPerson::Camera::Active() {
    uint8_t* manager = Manager();
    return manager != nullptr ? g_activeCamera(manager) : nullptr;
}

bool ThirdPerson::Camera::Place(float out[3]) {
    uint8_t* camera = Active();
    uint8_t* entity = camera != nullptr ? Pawn::Entity(camera) : nullptr;
    if (entity == nullptr) {
        return false;
    }
    std::copy_n(Pawn::Place(entity), 3, out);
    return true;
}

uint8_t* ThirdPerson::Camera::Activate(const char* name) {
    uint8_t* manager = g_activateByName ? Manager() : nullptr;
    if (manager == nullptr || Field<uint8_t>(manager, kManagerLocked) != 0) {
        return nullptr;
    }
    uint8_t* before = g_activeCamera(manager);
    g_activateByName(manager, nullptr, name, 1);
    uint8_t* after = g_activeCamera(manager);
    return after != before ? after : nullptr;
}
