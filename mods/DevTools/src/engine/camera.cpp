// The camera manager, and the free camera prototype it can be asked for.
#include "engine/camera.h"

#include "engine/entity.h"
#include "engine/player.h"
#include "fcse_api.h"

namespace {
    constexpr ptrdiff_t kManagerLocked = 0x14;
    constexpr size_t kSetFocusSlot = 0x78 / sizeof(void*);

    // CCameraFreeComponent; CCameraGhostComponent derives from it without adding fields.
    constexpr ptrdiff_t kMoveForward = 0xB4;
    constexpr ptrdiff_t kMoveStrafe = 0xB8;
    constexpr ptrdiff_t kMoveVertical = 0xBC;
    constexpr ptrdiff_t kLookYaw = 0xC0;
    constexpr ptrdiff_t kLookPitch = 0xC4;
    constexpr ptrdiff_t kSpeed = 0xC8;
    constexpr ptrdiff_t kSpeedAdjust = 0xCC;

    // The euler triple the renderer reads off the render camera; pitch is the first of the three.
    constexpr ptrdiff_t kRenderCameraEuler = 0x6C;

    using ActivateByNameFn = void(__fastcall*)(void* manager, void* unused, const char* name,
                                               int32_t notify);
    using ActiveCameraFn = void*(__fastcall*)(void* manager);
    using RenderCameraFn = void*(__fastcall*)(void* camera);
    using SetFocusFn = void(__fastcall*)(void* camera, void* unused, void* entityRef);
    using FreeCameraUpdateFn = void(__fastcall*)(void* self, void* unused, float delta,
                                                 uint32_t flags);

    FCSE::Relocation<ActivateByNameFn> g_activateByNameSite{FCSE::Uplay(0x0057CDE0)};
    FCSE::Relocation<ActiveCameraFn> g_activeCameraSite{FCSE::Uplay(0x0057C1B0)};
    FCSE::Relocation<RenderCameraFn> g_renderCameraSite{FCSE::Uplay(0x00504CE0)};
    FCSE::Relocation<FreeCameraUpdateFn> g_freeCameraUpdate{FCSE::Uplay(0x00692050)};

    // Resolved once in Install: these are read every frame, and an unresolved Relocation re-runs a
    // full scan of Dunia's code section on every access.
    ActivateByNameFn g_activateByName = nullptr;
    ActiveCameraFn g_activeCamera = nullptr;
    RenderCameraFn g_renderCamera = nullptr;

    FreeCameraUpdateFn g_originalFreeCameraUpdate = nullptr;
    DevTools::Camera::UpdateFn g_onUpdate = nullptr;

    float* Field(void* camera, ptrdiff_t offset) {
        return reinterpret_cast<float*>(static_cast<uint8_t*>(camera) + offset);
    }

    // ECX is the component's second base rather than its start, four bytes in.
    void __fastcall FreeCameraUpdateDetour(void* self, void* unused, float delta, uint32_t flags) {
        if (g_onUpdate != nullptr) {
            g_onUpdate(static_cast<uint8_t*>(self) - 4);
        }
        g_originalFreeCameraUpdate(self, unused, delta, flags);
    }
}

namespace DevTools::Camera {

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_activateByNameSite || !g_activeCameraSite) {
        api->Log("camera: the camera manager's entry points were not found in this build");
        return;
    }

    g_activateByName = g_activateByNameSite.get();
    g_activeCamera = g_activeCameraSite.get();
    g_renderCamera = g_renderCameraSite ? g_renderCameraSite.get() : nullptr;

    if (!g_freeCameraUpdate ||
        !api->Hook(reinterpret_cast<void*>(g_freeCameraUpdate.address()),
                   reinterpret_cast<void*>(&FreeCameraUpdateDetour),
                   reinterpret_cast<void**>(&g_originalFreeCameraUpdate))) {
        api->Log("camera: the free camera's update was not hooked - a free camera would not steer");
    }
}

void* Active(void* manager) {
    return (manager != nullptr && g_activeCamera != nullptr) ? g_activeCamera(manager) : nullptr;
}

void ActivateByName(void* manager, const char* name) {
    if (manager != nullptr && g_activateByName != nullptr) {
        g_activateByName(manager, nullptr, name, 1);
    }
}

uint8_t* Locked(void* manager) {
    return manager != nullptr ? static_cast<uint8_t*>(manager) + kManagerLocked : nullptr;
}

void SnapToFocus(void* manager, void* camera) {
    if (camera == nullptr) {
        return;
    }

    void* ref = Entity::AcquireFocusRef(manager);
    if (ref == nullptr) {
        return;
    }

    if (Entity::Of(ref) != nullptr) {
        // Taken by value: the callee drops this reference, leaving the one acquired above.
        Entity::AddRef(ref);

        auto** vtable = *reinterpret_cast<void***>(camera);
        reinterpret_cast<SetFocusFn>(vtable[kSetFocusSlot])(camera, nullptr, ref);
    }

    Entity::ReleaseRef(ref);
}

bool ViewPitch(void* manager, float& pitch) {
    if (g_renderCamera == nullptr) {
        return false;
    }

    void* camera = Active(manager);
    if (camera == nullptr) {
        return false;
    }

    auto* render = static_cast<uint8_t*>(g_renderCamera(camera));
    if (render == nullptr) {
        return false;
    }

    pitch = *reinterpret_cast<float*>(render + kRenderCameraEuler);
    return true;
}

void ClearAxes(void* camera) {
    if (camera == nullptr) {
        return;
    }

    SetMove(camera, 0.0f, 0.0f, 0.0f);
    SetLook(camera, 0.0f, 0.0f);
    *Field(camera, kSpeedAdjust) = 0.0f;
}

void SetMove(void* camera, float forward, float strafe, float vertical) {
    *Field(camera, kMoveForward) = forward;
    *Field(camera, kMoveStrafe) = strafe;
    *Field(camera, kMoveVertical) = vertical;
}

void SetLook(void* camera, float yaw, float pitch) {
    *Field(camera, kLookYaw) = yaw;
    *Field(camera, kLookPitch) = pitch;
}

void SetSpeed(void* camera, float metresPerSecond) {
    *Field(camera, kSpeed) = metresPerSecond;
}

void OnUpdate(UpdateFn fn) { g_onUpdate = fn; }

}
