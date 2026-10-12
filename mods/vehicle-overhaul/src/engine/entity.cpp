// Entities, reached from one of their components.
#include "engine/entity.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    // A component's entity holder, the entity in it, and the entity's transform.
    constexpr ptrdiff_t kComponentRef = 0x08;
    constexpr ptrdiff_t kRefEntity = 0x0C;
    constexpr ptrdiff_t kEntityMatrix = 0x30;

    // A CSkeletonObject's bones: each a CStringID name and, at kBoneModel, its 4x4 in model space.
    constexpr ptrdiff_t kSkeletonBones = 0x20;
    constexpr ptrdiff_t kSkeletonBoneCount = 0x2C;
    constexpr ptrdiff_t kBoneStride = 0xA0;
    constexpr ptrdiff_t kBoneModel = 0x50;
    // CGraphicComponent::GetSkeleton.
    constexpr size_t kGetSkeletonSlot = 0x78 / sizeof(void*);

    using FlushJobFn = void(__fastcall*)(void* entity);
    using Vec3Fn = void(__fastcall*)(void* entity, void* unused, float a, float b, float c);
    using ComponentFn = void*(__fastcall*)(void* self);

    FCSE::Relocation<FlushJobFn> g_flushJob{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<Vec3Fn> g_setPosition{FCSE::Uplay(0x004E01B0)};
    FCSE::Relocation<Vec3Fn> g_setEuler{FCSE::Uplay(0x004E01C0)};
    // CEntity::GetComponent<CGraphicComponent>.
    FCSE::Relocation<ComponentFn> g_graphic{FCSE::Uplay(0x00060BA0)};

    const float* BoneModel(void* entity, uint32_t bone) {
        g_flushJob(entity);
        void* graphic = g_graphic(entity);
        if (graphic == nullptr) {
            return nullptr;
        }
        auto* getSkeleton = reinterpret_cast<ComponentFn>((*reinterpret_cast<void***>(graphic))[kGetSkeletonSlot]);
        const auto* skeleton = static_cast<const uint8_t*>(getSkeleton(graphic));
        if (skeleton == nullptr) {
            return nullptr;
        }
        const auto* bones = *reinterpret_cast<const uint8_t* const*>(skeleton + kSkeletonBones);
        const uint32_t count = *reinterpret_cast<const uint32_t*>(skeleton + kSkeletonBoneCount);
        for (uint32_t i = 0; i < count; i++) {
            const uint8_t* node = bones + i * kBoneStride;
            if (*reinterpret_cast<const uint32_t*>(node) == bone) {
                return reinterpret_cast<const float*>(node + kBoneModel);
            }
        }
        return nullptr;
    }
}

namespace VehicleOverhaul::Entity {

bool Install() {
    if (!g_flushJob || !g_setPosition || !g_setEuler || !g_graphic) {
        FCSE::ApiPointer()->Log("entity: the calls that move an entity or find its skeleton were not found in this build");
        return false;
    }
    return true;
}

void* Of(void* component) {
    if (component == nullptr) {
        return nullptr;
    }
    auto* ref = *reinterpret_cast<uint8_t**>(static_cast<uint8_t*>(component) + kComponentRef);
    return ref != nullptr ? *reinterpret_cast<void**>(ref + kRefEntity) : nullptr;
}

const float* Matrix(void* entity) {
    return reinterpret_cast<const float*>(static_cast<uint8_t*>(entity) + kEntityMatrix);
}

void Place(void* entity, const float* position, const float* angles) {
    g_flushJob(entity);
    g_setPosition(entity, nullptr, position[0], position[1], position[2]);
    g_setEuler(entity, nullptr, angles[0], angles[1], angles[2]);
}

bool Bone(void* entity, uint32_t bone, float* position, float* forward) {
    const float* model = BoneModel(entity, bone);
    if (model == nullptr) {
        return false;
    }
    const float* world = Matrix(entity);
    for (int axis = 0; axis < 3; axis++) {
        position[axis] = model[12] * world[axis] + model[13] * world[4 + axis] +
                         model[14] * world[8 + axis] + world[12 + axis];
        forward[axis] = model[4] * world[axis] + model[5] * world[4 + axis] + model[6] * world[8 + axis];
    }
    return true;
}

}
