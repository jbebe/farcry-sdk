#include "level.h"

#include "fcse_api.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace MapOverhaul::Level {

namespace {
    using UpdateFn = void(__fastcall*)(void* strategy, void* unused, float seconds);
    using GetWielderFn = uint8_t*(__fastcall*)(void* equipment, void* unused);
    using CategoryFn = const uint32_t*(__fastcall*)(void* equipment, void* unused);
    using GraphicSkeletonFn = uint8_t*(__fastcall*)(uint8_t* graphic);
    using PoseFn = void(__fastcall*)(uint8_t* skeleton);
    using PoseJobFn = void(__cdecl*)(uint32_t** context);

    // CGadgetMapStrategy's Update, every frame for each map-like gadget, found past its first two
    // instructions, which Inventory Overhaul's hook overwrites; and CEquipmentBase::GetWielder.
    FCSE::Relocation<uint8_t*> g_updateBody{FCSE::Pattern(
        "53 55 56 57 51 D9 1C 24 8B D9 E8 ?? ?? ?? ?? 8B 83 70 01 00 00 8B 30")};
    constexpr ptrdiff_t kUpdateEntry = -7;
    FCSE::Relocation<GetWielderFn> g_getWielder{FCSE::Uplay(0x00156BA0)};
    // A skeleton posed while marked dirty, and its posing as a job every frame, the job's first field its
    // declaration: four counts of buffers, then the buffers, the skeleton after the bones.
    FCSE::Relocation<PoseFn> g_poseSite{FCSE::Uplay(0x003322C0)};
    FCSE::Relocation<PoseJobFn> g_poseJobSite{FCSE::Uplay(0x0064EC50)};
    UpdateFn g_update = nullptr;
    PoseFn g_pose = nullptr;
    PoseJobFn g_poseJob = nullptr;

    // The strategy's equipment and the equipment's category, the map's being "map"; the pawn's data, its
    // body's graphic component and that one's skeleton.
    constexpr ptrdiff_t kStrategyEquipment = 0x38;
    constexpr size_t kCategorySlot = 0x8C / sizeof(void*);
    constexpr uint32_t kMap = FCSE::Crc32("map");
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataBodyGraphic = 0x0C;
    constexpr size_t kGraphicSkeletonSlot = 0x78 / sizeof(void*);
    // A skeleton's bones and their count; a bone's name, posed 4x4 (axis rows right, ahead, up, then its
    // place) and parent.
    constexpr ptrdiff_t kBones = 0x20;
    constexpr ptrdiff_t kBoneCount = 0x2C;
    constexpr ptrdiff_t kBoneSize = 0xA0;
    constexpr ptrdiff_t kBoneModel = 0x50;
    constexpr ptrdiff_t kBoneParent = 0x90;
    constexpr uint32_t kSpine = FCSE::Crc32("Spine");
    constexpr uint32_t kCamera = FCSE::Crc32("Camera");
    constexpr size_t kMostBones = 256;

    // Seconds the hold takes to come on as the map comes out and to go as it is put away.
    constexpr float kEase = 0.25f;

    std::atomic<uint8_t*> g_skeleton{nullptr};
    int g_camera = -1;
    std::array<bool, kMostBones> g_turned = {};
    std::atomic<float> g_weight{0.0f};
    // What the last turn left in each bone: a bone still holding it was not posed again since.
    std::array<std::array<float, 16>, kMostBones> g_written = {};

    template <class T>
    T& Field(const void* object, ptrdiff_t offset) {
        return *reinterpret_cast<T*>(static_cast<uint8_t*>(const_cast<void*>(object)) + offset);
    }

    uint8_t* BoneAt(uint8_t* skeleton, uint32_t index) {
        return Field<uint8_t*>(skeleton, kBones) + index * kBoneSize;
    }

    float* Model(uint8_t* skeleton, uint32_t index) {
        return &Field<float>(BoneAt(skeleton, index), kBoneModel);
    }

    // The view's bone, and the spine and every bone below it but the view's, bones coming after their parents.
    bool FindBones(uint8_t* skeleton) {
        const uint32_t count = Field<uint32_t>(skeleton, kBoneCount);
        if (count > kMostBones) {
            return false;
        }
        g_camera = -1;
        g_turned.fill(false);
        for (uint32_t i = 0; i < count; ++i) {
            const uint32_t name = Field<uint32_t>(BoneAt(skeleton, i), 0);
            const int parent = Field<int>(BoneAt(skeleton, i), kBoneParent);
            g_turned[i] = name == kSpine || (parent >= 0 && static_cast<uint32_t>(parent) < i && g_turned[parent]);
            if (name == kCamera) {
                g_camera = static_cast<int>(i);
            }
        }
        if (g_camera < 0) {
            return false;
        }
        g_turned[g_camera] = false;
        return true;
    }

    uint8_t* SkeletonOf(uint8_t* pawn) {
        uint8_t* graphic = Field<uint8_t*>(Field<uint8_t*>(pawn, kPawnData), kDataBodyGraphic);
        return graphic != nullptr ? Field<GraphicSkeletonFn*>(graphic, 0)[kGraphicSkeletonSlot](graphic) : nullptr;
    }

    void __fastcall OnUpdate(void* strategy, void* unused, float seconds) {
        g_update(strategy, unused, seconds);
        void* equipment = Field<void*>(strategy, kStrategyEquipment);
        if (equipment == nullptr || *Field<CategoryFn*>(equipment, 0)[kCategorySlot](equipment, nullptr) != kMap) {
            return;
        }
        uint8_t* pawn = g_getWielder(equipment, nullptr);
        uint8_t* skeleton = pawn != nullptr ? SkeletonOf(pawn) : nullptr;
        if (skeleton != nullptr && skeleton != g_skeleton) {
            g_skeleton = nullptr;
            if (!FindBones(skeleton)) {
                return;
            }
            g_skeleton = skeleton;
        }
        const float step = std::clamp(seconds / kEase, 0.0f, 1.0f);
        const float weight = g_weight;
        g_weight = skeleton != nullptr ? std::min(1.0f, weight + step) : std::max(0.0f, weight - step);
    }

    // Turns the upper body back about the eye by the view's pitch, as far as the hold has come on.
    void Hold(uint8_t* skeleton) {
        const float weight = g_weight;
        if (skeleton != g_skeleton || weight <= 0.0f) {
            return;
        }
        const float* eye = Model(skeleton, g_camera);
        const float pitch = std::asin(std::clamp(eye[6], -1.0f, 1.0f));
        const float angle = -pitch * weight;
        const float u[3] = {eye[0], eye[1], eye[2]};
        const float length = std::sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        if (length < 1e-6f || std::fabs(angle) < 1e-5f) {
            return;
        }
        // About the view's right axis, through the eye.
        const float x = u[0] / length, y = u[1] / length, z = u[2] / length;
        const float c = std::cos(angle), s = std::sin(angle), t = 1.0f - c;
        const float turn[3][3] = {{t * x * x + c, t * x * y - s * z, t * x * z + s * y},
                                  {t * x * y + s * z, t * y * y + c, t * y * z - s * x},
                                  {t * x * z - s * y, t * y * z + s * x, t * z * z + c}};
        const float pivot[3] = {eye[12], eye[13], eye[14]};
        auto rotate = [&](float* v) {
            const float was[3] = {v[0], v[1], v[2]};
            for (int k = 0; k < 3; ++k) {
                v[k] = turn[k][0] * was[0] + turn[k][1] * was[1] + turn[k][2] * was[2];
            }
        };
        const uint32_t count = Field<uint32_t>(skeleton, kBoneCount);
        for (uint32_t i = 0; i < count && i < kMostBones; ++i) {
            float* model = Model(skeleton, i);
            if (!g_turned[i] || std::equal(model, model + 16, g_written[i].begin())) {
                continue;
            }
            rotate(model);
            rotate(model + 4);
            rotate(model + 8);
            float* place = model + 12;
            for (int k = 0; k < 3; ++k) {
                place[k] -= pivot[k];
            }
            rotate(place);
            for (int k = 0; k < 3; ++k) {
                place[k] += pivot[k];
            }
            std::copy_n(model, 16, g_written[i].begin());
        }
    }

    void __fastcall OnPose(uint8_t* skeleton) {
        const bool dirty = (Field<uint32_t>(skeleton, 0x30) & 1) != 0;
        g_pose(skeleton);
        if (dirty) {
            Hold(skeleton);
        }
    }

    void __cdecl OnPoseJob(uint32_t** context) {
        g_poseJob(context);
        const uint32_t* job = *context;
        Hold(reinterpret_cast<uint8_t*>(job[5 + job[0] + job[1] + job[2]]));
    }

    template <class Fn>
    bool Hook(uintptr_t site, Fn detour, Fn* original) {
        return site != 0 && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site), reinterpret_cast<void*>(detour),
                                                     reinterpret_cast<void**>(original));
    }
}

bool Install() {
    const bool hooked = g_updateBody && g_getWielder &&
                        Hook(reinterpret_cast<uintptr_t>(g_updateBody.get() + kUpdateEntry), &OnUpdate, &g_update) &&
                        Hook(g_poseSite.address(), &OnPose, &g_pose) && Hook(g_poseJobSite.address(), &OnPoseJob, &g_poseJob);
    FCSE::Logf("level: %s", hooked ? "the sheet stays level as the view looks up and down"
                                    : "off, the map's update or the posing was not found");
    return hooked;
}

}
