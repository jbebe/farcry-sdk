// One of the engine's own scene lights, owned the way the player's vehicle owns its headlight, and
// the camera it is placed from.
//
// The light container and the two calls into it are the ones CVehicleTypeWheeled::
// EnableFrontHeadlights uses, read out of its instructions.
#include "engine/light.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstdint>

namespace {
    struct Handle {
        int32_t index;
        int32_t generation;
    };

    using CreateFn = Handle*(__thiscall*)(void* container, Handle* out, bool flag);
    using ModifyFn = uint8_t*(__thiscall*)(void* container, Handle* handle, bool flag);
    using DestroyFn = void(__thiscall*)(void* container, int32_t index, int32_t generation);
    using ActiveCameraFn = void*(__thiscall*)(void* manager);
    using RenderCameraFn = uint8_t*(__thiscall*)(void* camera);

    FCSE::Relocation<uint8_t*> g_headlight{FCSE::Uplay(0x001AE6C0)};
    FCSE::Relocation<DestroyFn> g_destroy{FCSE::Uplay(0x00459550)};
    FCSE::Relocation<ActiveCameraFn> g_activeCamera{FCSE::Uplay(0x0057C1B0)};
    FCSE::Relocation<RenderCameraFn> g_renderCamera{FCSE::Uplay(0x00504CE0)};

    // In EnableFrontHeadlights: `mov ecx, <container>`, `call CreateObject`, `call ModifyOriginal`.
    constexpr size_t kContainerImmediate = 0x42;
    constexpr size_t kCreateCall = 0x46;
    constexpr size_t kModifyCall = 0x5F;
    constexpr uint8_t kMovEcx = 0xB9;
    constexpr uint8_t kCall = 0xE8;

    // CSceneLight.
    constexpr ptrdiff_t kType = 0x04;
    constexpr ptrdiff_t kEnabled = 0x08;
    constexpr ptrdiff_t kPosition = 0x0C;
    constexpr ptrdiff_t kDirection = 0x18;
    constexpr ptrdiff_t kUp = 0x24;
    constexpr ptrdiff_t kRange = 0x30;
    constexpr ptrdiff_t kColour = 0x34;
    constexpr ptrdiff_t kIntensity = 0x40;
    constexpr ptrdiff_t kCastShadow = 0x48;
    constexpr ptrdiff_t kShadowFactor = 0x4C;
    constexpr ptrdiff_t kOuterAngle = 0x5C;
    constexpr ptrdiff_t kInnerAngle = 0x60;
    constexpr int32_t kSpot = 3;

    // The render camera's position and axes, as CSceneCamera lays them out.
    constexpr ptrdiff_t kCameraPosition = 0x08;
    constexpr ptrdiff_t kCameraFront = 0x48;
    constexpr ptrdiff_t kCameraUp = 0x54;
    constexpr ptrdiff_t kCameraRight = 0x60;

    // The local player's scene, and the camera manager embedded in it.
    constexpr ptrdiff_t kPlayerScene = 0x04;
    constexpr ptrdiff_t kSceneCameraManager = 0xB8;

    void* g_container = nullptr;
    CreateFn g_create = nullptr;
    ModifyFn g_modify = nullptr;

    Handle g_handle{-1, -1};

    // The light, writable: ModifyOriginal also marks it for the renderer to pick up.
    uint8_t* Writable() { return g_modify(g_container, &g_handle, true); }
}

namespace Flashlight::Light {

bool Install() {
    const uint8_t* headlight = g_headlight.get();
    if (headlight == nullptr || headlight[kContainerImmediate - 1] != kMovEcx ||
        headlight[kCreateCall] != kCall || headlight[kModifyCall] != kCall || !g_destroy ||
        !g_activeCamera || !g_renderCamera) {
        FCSE::ApiPointer()->Log("light: the light or camera calls were not found in this build");
        return false;
    }

    g_container = *reinterpret_cast<void* const*>(headlight + kContainerImmediate);
    g_create = CallTarget<CreateFn>(headlight + kCreateCall);
    g_modify = CallTarget<ModifyFn>(headlight + kModifyCall);
    return true;
}

void Configure(const Spot& spot) {
    if (g_handle.index == -1) {
        g_create(g_container, &g_handle, false);
    }
    uint8_t* light = Writable();
    Field<int32_t>(light, kType) = kSpot;
    Field<float>(light, kRange) = spot.range;
    SetVec3(light, kColour, spot.colour);
    Field<uint8_t>(light, kCastShadow) = spot.castShadow;
    Field<float>(light, kShadowFactor) = spot.shadowFactor;
    Field<float>(light, kOuterAngle) = spot.outerAngle;
    Field<float>(light, kInnerAngle) = spot.innerAngle;
}

void SetEnabled(bool enabled) {
    if (g_handle.index != -1) {
        Field<uint8_t>(Writable(), kEnabled) = enabled;
    }
}

std::optional<Pose> Place(void* player, float intensity, float above, float right) {
    uint8_t* scene = Field<uint8_t*>(static_cast<uint8_t*>(player), kPlayerScene);
    void* camera = scene != nullptr ? g_activeCamera(scene + kSceneCameraManager) : nullptr;
    const uint8_t* view = camera != nullptr ? g_renderCamera(camera) : nullptr;
    if (view == nullptr) {
        return std::nullopt;
    }

    const float* eye = Vec3(view, kCameraPosition);
    const float* up = Vec3(view, kCameraUp);
    const float* side = Vec3(view, kCameraRight);
    Pose pose;
    for (int axis = 0; axis < 3; ++axis) {
        pose.position[axis] = eye[axis] + up[axis] * above + side[axis] * right;
    }
    std::copy_n(Vec3(view, kCameraFront), 3, pose.direction);

    uint8_t* light = Writable();
    Field<float>(light, kIntensity) = intensity;
    SetVec3(light, kPosition, pose.position);
    SetVec3(light, kDirection, pose.direction);
    SetVec3(light, kUp, up);
    return pose;
}

void Destroy() {
    if (g_handle.index != -1) {
        g_destroy(g_container, g_handle.index, g_handle.generation);
        g_handle = {-1, -1};
    }
}

}
