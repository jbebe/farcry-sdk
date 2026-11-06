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
    using PlayerBody::Field;

    constexpr ptrdiff_t kPlayerScene = 0x04;
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;

    using LocalPlayerFn = uint8_t*(__cdecl*)();
    using ActiveCameraFn = uint8_t*(__fastcall*)(uint8_t* manager);
    FCSE::Relocation<LocalPlayerFn> g_localPlayer{FCSE::Uplay(0x00831870)};
    FCSE::Relocation<ActiveCameraFn> g_activeCamera{FCSE::Uplay(0x0057C1B0)};
}

bool PlayerBody::Camera::Install() {
    if (!g_localPlayer || !g_activeCamera) {
        FCSE::ApiPointer()->Log("camera: the camera manager's entry points were not found");
        return false;
    }
    return true;
}

bool PlayerBody::Camera::Place(float out[3]) {
    uint8_t* local = g_localPlayer && g_activeCamera ? g_localPlayer() : nullptr;
    uint8_t* scene = local != nullptr ? Field<uint8_t*>(local, kPlayerScene) : nullptr;
    uint8_t* camera = scene != nullptr ? g_activeCamera(scene + kSceneCameraManager) : nullptr;
    uint8_t* entity = camera != nullptr ? Pawn::Entity(camera) : nullptr;
    if (entity == nullptr) {
        return false;
    }
    std::copy_n(&Field<float>(entity, Pawn::kEntityMatrix) + 12, 3, out);
    return true;
}
