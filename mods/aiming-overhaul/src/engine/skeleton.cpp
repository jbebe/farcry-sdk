// A body's skeleton as the engine poses it: its bones, and each time they are posed anew.
//
// See docs/docs/engine-internals/first-person-aiming.md#leaning.
#include "engine/skeleton.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <cstddef>

namespace {
    using AimingOverhaul::Field;

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

    // An entity bound to a bone placed: the bone's posed matrix copied into the child binding.
    // __thiscall on the parent's binding, with the child's and a matrix used when there is no bone.
    using PlaceChildFn = void(__fastcall*)(uint8_t* parent, void* unused, uint8_t* child,
                                           const float* matrix);
    FCSE::Relocation<PlaceChildFn> g_placeChild{FCSE::Uplay(0x005530F0)};
    PlaceChildFn g_originalPlaceChild = nullptr;

    // A parent binding's skeleton, once looked up; a child's bone index, -1 for none, and the copy
    // of that bone's posed matrix.
    constexpr ptrdiff_t kParentSkeleton = 0xC0;
    constexpr ptrdiff_t kChildBone = 0x1C;
    constexpr ptrdiff_t kChildBoneMatrix = 0x40;

    // CGraphicComponent's skeleton through its vtable: to read, and to change, which marks its
    // bones to be posed again. __thiscall.
    using GraphicSkeletonFn = uint8_t*(__fastcall*)(uint8_t* graphic);
    constexpr ptrdiff_t kGraphicSkeletonSlot = 0x78 / 4;
    constexpr ptrdiff_t kGraphicChangeSkeletonSlot = 0x7C / 4;

    // In CPawn's data, the body's graphic component.
    constexpr ptrdiff_t kBodyGraphic = 0x0C;

    // The skeleton: its bones, how many, and its flags, bit 0 the bones awaiting a rebuild.
    constexpr ptrdiff_t kBones = 0x20;
    constexpr ptrdiff_t kBoneCount = 0x2C;
    constexpr ptrdiff_t kFlags = 0x30;
    // A bone: its name's CRC-32, posed matrix and parent index.
    constexpr ptrdiff_t kBoneSize = 0xA0;
    constexpr ptrdiff_t kBoneName = 0x00;
    constexpr ptrdiff_t kBoneModel = 0x50;
    constexpr ptrdiff_t kBoneParent = 0x90;

    AimingOverhaul::Skeleton::PosedFn g_posed = nullptr;
    AimingOverhaul::Skeleton::AttachedFn g_attached = nullptr;

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

    void __fastcall PlaceChildDetour(uint8_t* parent, void* unused, uint8_t* child,
                                     const float* matrix) {
        g_originalPlaceChild(parent, unused, child, matrix);
        const int bone = Field<int>(child, kChildBone);
        uint8_t* skeleton = Field<uint8_t*>(parent, kParentSkeleton);
        if (bone >= 0 && skeleton != nullptr) {
            g_attached(skeleton, bone, &Field<float>(child, kChildBoneMatrix));
        }
    }
}

uint8_t* AimingOverhaul::Skeleton::Of(uint8_t* pawn, bool repose) {
    uint8_t* graphic = Field<uint8_t*>(Pawn::Data(pawn), kBodyGraphic);
    if (graphic == nullptr) {
        return nullptr;
    }
    const ptrdiff_t slot = repose ? kGraphicChangeSkeletonSlot : kGraphicSkeletonSlot;
    return Field<GraphicSkeletonFn*>(graphic, 0)[slot](graphic);
}

uint32_t AimingOverhaul::Skeleton::Count(uint8_t* skeleton) {
    return Field<uint32_t>(skeleton, kBoneCount);
}

AimingOverhaul::Skeleton::Bone AimingOverhaul::Skeleton::At(uint8_t* skeleton, uint32_t index) {
    uint8_t* bone = BoneAt(skeleton, index);
    return {&Field<float>(bone, kBoneModel), Field<int>(bone, kBoneParent)};
}

int AimingOverhaul::Skeleton::Find(uint8_t* skeleton, uint32_t name) {
    const uint32_t count = Count(skeleton);
    for (uint32_t i = 0; i < count; ++i) {
        if (Field<uint32_t>(BoneAt(skeleton, i), kBoneName) == name) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

bool AimingOverhaul::Skeleton::Install(PosedFn posed, AttachedFn attached) {
    g_posed = posed;
    g_attached = attached;
    if (!Hook(g_pose.address(), &PoseDetour, &g_originalPose) ||
        !Hook(g_poseJob.address(), &PoseJobDetour, &g_originalPoseJob) ||
        !Hook(g_placeChild.address(), &PlaceChildDetour, &g_originalPlaceChild)) {
        FCSE::ApiPointer()->Log("skeleton: posing cannot be hooked, so the body does not lean");
        return false;
    }
    return true;
}
