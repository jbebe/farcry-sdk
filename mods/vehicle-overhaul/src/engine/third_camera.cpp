// CCameraThirdComponent's update, with the camera's placement handed to the plugin.
//
// See docs/docs/engine-internals/free-camera-and-noclip.md.
#include "engine/third_camera.h"

#include "engine/entity.h"
#include "fcse_api.h"

#include <cstdint>
#include <cstring>

namespace {
    // Off the update's `this`, which is the component's second base, four bytes in.
    constexpr ptrdiff_t kComponent = -4;
    constexpr ptrdiff_t kActive = 0x0C;
    constexpr ptrdiff_t kFov = 0x6C;

    // The scene camera ModifySceneCamera hands back.
    constexpr ptrdiff_t kScenePosition = 0x08;
    constexpr ptrdiff_t kSceneAngles = 0x04;
    constexpr ptrdiff_t kSceneFov = 0x28;
    constexpr ptrdiff_t kSceneFovTarget = 0x30;

    using UpdateFn = void(__fastcall*)(uint8_t* self, void* unused, float seconds, uint32_t flags);
    using BlendFn = void(__fastcall*)(uint8_t* component, void* unused, float* position, float* angles,
                                      float seconds);
    using SceneCameraFn = uint8_t*(__fastcall*)(uint8_t* component);
    using SetAnglesFn = void(__fastcall*)(uint8_t* camera, void* unused, const float* angles);

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x00695BC0)};
    FCSE::Relocation<UpdateFn> g_baseUpdate{FCSE::Uplay(0x00504D50)};
    FCSE::Relocation<BlendFn> g_blend{FCSE::Uplay(0x00505460)};
    FCSE::Relocation<SceneCameraFn> g_sceneCamera{FCSE::Uplay(0x00504CE0)};
    FCSE::Relocation<SetAnglesFn> g_setAngles{FCSE::Uplay(0x00D108C0)};

    UpdateFn g_original = nullptr;
    VehicleOverhaul::ThirdCamera::PoseFn g_pose = nullptr;

    // A placed camera skips the engine's update whole, and with it the full-body request on the pawn.
    void __fastcall UpdateDetour(uint8_t* self, void* unused, float seconds, uint32_t flags) {
        uint8_t* component = self + kComponent;
        VehicleOverhaul::ThirdCamera::Pose pose{};
        if (!g_pose(component, seconds, pose)) {
            g_original(self, unused, seconds, flags);
            return;
        }

        if (self[kActive] != 0) {
            g_blend(component, nullptr, pose.position, pose.angles, seconds);
        }
        if (void* entity = VehicleOverhaul::Entity::Of(component)) {
            VehicleOverhaul::Entity::Place(entity, pose.position, pose.angles);
        }

        uint8_t* scene = g_sceneCamera(component);
        std::memcpy(scene + kScenePosition, pose.position, sizeof(pose.position));
        g_setAngles(scene + kSceneAngles, nullptr, pose.angles);
        const float fov = *reinterpret_cast<float*>(self + kFov);
        *reinterpret_cast<float*>(scene + kSceneFov) = fov;
        *reinterpret_cast<float*>(scene + kSceneFovTarget) = fov;

        g_baseUpdate(self, unused, seconds, flags);
    }
}

namespace VehicleOverhaul::ThirdCamera {

bool Install(PoseFn pose) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_update || !g_baseUpdate || !g_blend || !g_sceneCamera || !g_setAngles) {
        api->Log("third camera: the camera update's entry points were not found in this build");
        return false;
    }
    g_pose = pose;
    return api->Hook(reinterpret_cast<void*>(g_update.address()), reinterpret_cast<void*>(&UpdateDetour),
                     reinterpret_cast<void**>(&g_original));
}

}
