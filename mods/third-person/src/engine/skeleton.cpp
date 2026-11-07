// A body's skeleton as the engine poses it. See skeleton.h.
#include "engine/skeleton.h"

#include "engine/memory.h"
#include "fcse_api.h"

#include <cstddef>

namespace {
    using ThirdPerson::Field;

    // The skeleton's bone matrices rebuilt, each bone's from its animated one and its parent's,
    // while the skeleton is marked dirty. __fastcall on the skeleton.
    using PoseFn = void(__fastcall*)(uint8_t* skeleton);
    FCSE::Relocation<PoseFn> g_pose{FCSE::Uplay(0x003322C0)};
    PoseFn g_originalPose = nullptr;

    // The same, as the skeleton's job, every frame the skeleton is wanted. __cdecl on the job's
    // context, whose first field is its declaration: four counts of buffers, then the buffers.
    using PoseJobFn = void(__cdecl*)(uint32_t** context);
    FCSE::Relocation<PoseJobFn> g_poseJob{FCSE::Uplay(0x0064EC50)};
    PoseJobFn g_originalPoseJob = nullptr;

    // The skeleton: its bones, how many, and its flags, bit 0 the bones awaiting a rebuild.
    constexpr ptrdiff_t kBones = 0x20;
    constexpr ptrdiff_t kBoneCount = 0x2C;
    constexpr ptrdiff_t kFlags = 0x30;
    // A bone: its name's CRC-32, posed matrix and parent index.
    constexpr ptrdiff_t kBoneSize = 0xA0;
    constexpr ptrdiff_t kBoneName = 0x00;
    constexpr ptrdiff_t kBoneModel = 0x50;
    constexpr ptrdiff_t kBoneParent = 0x90;

    ThirdPerson::Skeleton::PosedFn g_posed = nullptr;

    uint8_t* BoneAt(uint8_t* skeleton, uint32_t index) {
        return Field<uint8_t*>(skeleton, kBones) + index * kBoneSize;
    }

    void __fastcall PoseDetour(uint8_t* skeleton) {
        const bool dirty = (Field<uint32_t>(skeleton, kFlags) & 1) != 0;
        g_originalPose(skeleton);
        if (dirty) {
            g_posed(skeleton);
        }
    }

    // The job declares the bones, then the skeleton they belong to.
    void __cdecl PoseJobDetour(uint32_t** context) {
        g_originalPoseJob(context);
        const uint32_t* job = *context;
        g_posed(reinterpret_cast<uint8_t*>(job[5 + job[0] + job[1] + job[2]]));
    }
}

uint32_t ThirdPerson::Skeleton::Count(uint8_t* skeleton) {
    return Field<uint32_t>(skeleton, kBoneCount);
}

uint8_t* ThirdPerson::Skeleton::Bones(uint8_t* skeleton) {
    return Field<uint8_t*>(skeleton, kBones);
}

int ThirdPerson::Skeleton::Parent(uint8_t* skeleton, uint32_t index) {
    return Field<int>(BoneAt(skeleton, index), kBoneParent);
}

float* ThirdPerson::Skeleton::Posed(uint8_t* skeleton, uint32_t index) {
    return &Field<float>(BoneAt(skeleton, index), kBoneModel);
}

int ThirdPerson::Skeleton::Find(uint8_t* skeleton, uint32_t name) {
    const uint32_t count = Count(skeleton);
    for (uint32_t i = 0; i < count; ++i) {
        if (Field<uint32_t>(BoneAt(skeleton, i), kBoneName) == name) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

const float* ThirdPerson::Skeleton::PosedIn(const uint8_t* bones, int32_t index) {
    return reinterpret_cast<const float*>(bones + index * kBoneSize + kBoneModel);
}

bool ThirdPerson::Skeleton::Install(PosedFn posed) {
    g_posed = posed;
    if (!Hook(g_pose.address(), &PoseDetour, &g_originalPose) ||
        !Hook(g_poseJob.address(), &PoseJobDetour, &g_originalPoseJob)) {
        FCSE::ApiPointer()->Log("skeleton: posing cannot be hooked, so the legs are not posed");
        return false;
    }
    return true;
}
