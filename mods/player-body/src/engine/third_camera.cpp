#include "engine/third_camera.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <cstring>

namespace {
    using PlayerBody::Field;

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
    using AnglesFn = void(__fastcall*)(uint8_t* camera, void* unused, const float* angles);
    using FlushJobFn = void(__fastcall*)(uint8_t* entity);
    using Vec3Fn = void(__fastcall*)(uint8_t* entity, void* unused, float a, float b, float c);

    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x00695BC0)};
    FCSE::Relocation<UpdateFn> g_baseUpdate{FCSE::Uplay(0x00504D50)};
    FCSE::Relocation<BlendFn> g_blend{FCSE::Uplay(0x00505460)};
    FCSE::Relocation<SceneCameraFn> g_sceneCamera{FCSE::Uplay(0x00504CE0)};
    FCSE::Relocation<AnglesFn> g_setAngles{FCSE::Uplay(0x00D108C0)};
    FCSE::Relocation<FlushJobFn> g_flushJob{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<Vec3Fn> g_setPosition{FCSE::Uplay(0x004E01B0)};
    FCSE::Relocation<Vec3Fn> g_setEuler{FCSE::Uplay(0x004E01C0)};

    UpdateFn g_original = nullptr;
    PlayerBody::ThirdCamera::PoseFn g_pose = nullptr;

    // A placed camera skips the engine's update whole, the full-body request on the pawn included.
    void __fastcall UpdateDetour(uint8_t* self, void* unused, float seconds, uint32_t flags) {
        uint8_t* component = self + kComponent;
        PlayerBody::ThirdCamera::Pose pose{};
        if (!g_pose(component, seconds, pose)) {
            g_original(self, unused, seconds, flags);
            return;
        }
        if (self[kActive] != 0) {
            g_blend(component, nullptr, pose.position, pose.angles, seconds);
        }
        if (uint8_t* entity = PlayerBody::Pawn::Entity(component)) {
            g_flushJob(entity);
            g_setPosition(entity, nullptr, pose.position[0], pose.position[1], pose.position[2]);
            g_setEuler(entity, nullptr, pose.angles[0], pose.angles[1], pose.angles[2]);
        }
        uint8_t* scene = g_sceneCamera(component);
        std::memcpy(scene + kScenePosition, pose.position, sizeof(pose.position));
        g_setAngles(scene + kSceneAngles, nullptr, pose.angles);
        Field<float>(scene, kSceneFov) = Field<float>(self, kFov);
        Field<float>(scene, kSceneFovTarget) = Field<float>(self, kFov);
        g_baseUpdate(self, unused, seconds, flags);
    }
}

bool PlayerBody::ThirdCamera::Install(PoseFn pose) {
    if (!g_update || !g_baseUpdate || !g_blend || !g_sceneCamera || !g_setAngles || !g_flushJob ||
        !g_setPosition || !g_setEuler) {
        FCSE::ApiPointer()->Log("third camera: the camera update's entry points were not found");
        return false;
    }
    g_pose = pose;
    return Hook(g_update.address(), &UpdateDetour, &g_original);
}
