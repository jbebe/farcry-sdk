// Leaning: held Q or E bends the body out to the side from the belly, and the head, the view, the
// arms and the gun go with it; see docs/docs/engine-internals/first-person-aiming.md#leaning.
#include "lean.h"

#include "ease.h"
#include "engine/skeleton.h"
#include "fcse_api.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>

namespace {
    constexpr uint32_t kStartLeft = FCSE::Crc32("start_lean_left");
    constexpr uint32_t kStopLeft = FCSE::Crc32("stop_lean_left");
    constexpr uint32_t kStartRight = FCSE::Crc32("start_lean_right");
    constexpr uint32_t kStopRight = FCSE::Crc32("stop_lean_right");

    // The bone the body bends at, everything above it bending with it, and the one the view follows.
    constexpr uint32_t kSpine = FCSE::Crc32("Spine");
    constexpr uint32_t kCamera = FCSE::Crc32("Camera");

    // Radians at full lean, and seconds for the bend to follow, which takes it most of the way in a
    // quarter of a second.
    constexpr float kMostBend = 0.21f;
    constexpr float kFollow = 0.08f;

    std::atomic<bool> g_enabled{true};
    bool g_left = false;
    bool g_right = false;
    float g_bend = 0.0f;

    // The skeleton leaned, its bend's bone and the view's, and which bones bend; and the bend in
    // radians, right positive, that posing it applies.
    constexpr size_t kMostBones = 256;
    std::atomic<uint8_t*> g_skeleton{nullptr};
    int g_spine = -1;
    int g_camera = -1;
    std::array<bool, kMostBones> g_bends = {};
    std::atomic<float> g_posedBend{0.0f};

    // Turns the point or axis `v`, in the body's axes, by `bend` about its forward axis.
    void Turn(float* v, float bend) {
        const float c = std::cos(bend);
        const float s = std::sin(bend);
        const float x = v[0];
        v[0] = x * c + v[2] * s;
        v[2] = v[2] * c - x * s;
    }

    // Notes which of the skeleton's bones bend: the spine's and every one below it in the tree.
    bool FindBends(uint8_t* skeleton) {
        using namespace AimingOverhaul;
        const uint32_t count = Skeleton::Count(skeleton);
        g_spine = Skeleton::Find(skeleton, kSpine);
        g_camera = Skeleton::Find(skeleton, kCamera);
        if (g_spine < 0 || g_camera < 0 || count > kMostBones) {
            return false;
        }
        for (uint32_t i = 0; i < count; ++i) {
            const int parent = Skeleton::At(skeleton, i).parent;
            g_bends[i] = static_cast<int>(i) == g_spine ||
                         (parent >= 0 && static_cast<uint32_t>(parent) < i && g_bends[parent]);
        }
        return true;
    }

    // How far the bend has moved the view's bone, in the view's axes, from where the skeleton holds
    // it now, bent by g_posedBend.
    AimingOverhaul::Aim::Offset EyeMoved(uint8_t* skeleton) {
        using namespace AimingOverhaul;
        const float* pivot = Skeleton::At(skeleton, g_spine).model + 12;
        const float* eye = Skeleton::At(skeleton, g_camera).model + 12;
        const float bend = g_posedBend;
        // The eye unbent, against the pivot, and that turned into the bent view's axes.
        float unbent[3] = {eye[0] - pivot[0], eye[1] - pivot[1], eye[2] - pivot[2]};
        Turn(unbent, -bend);
        float back[3] = {unbent[0], unbent[1], unbent[2]};
        Turn(back, -bend);
        return {unbent[0] - back[0], unbent[2] - back[2]};
    }
}

void AimingOverhaul::Lean::OnSignal(uint32_t signal) {
    if (signal == kStartLeft) {
        g_left = true;
    } else if (signal == kStopLeft) {
        g_left = false;
    } else if (signal == kStartRight) {
        g_right = true;
    } else if (signal == kStopRight) {
        g_right = false;
    }
}

AimingOverhaul::Aim::Offset AimingOverhaul::Lean::Update(uint8_t* pawn, float seconds, bool free) {
    float target = 0.0f;
    if (g_enabled && free) {
        target = kMostBend * ((g_right ? 1.0f : 0.0f) - (g_left ? 1.0f : 0.0f));
    }
    const bool moving = g_bend != 0.0f || target != 0.0f;
    g_bend = Follow(g_bend, target, seconds, kFollow);
    if (target == 0.0f && std::fabs(g_bend) < 1e-4f) {
        g_bend = 0.0f;
    }

    uint8_t* skeleton = Skeleton::Of(pawn, moving);
    if (skeleton != g_skeleton) {
        g_skeleton = nullptr;
        g_posedBend = 0.0f;
        if (skeleton == nullptr || !FindBends(skeleton)) {
            return {};
        }
        g_skeleton = skeleton;
    }
    const Aim::Offset moved = EyeMoved(skeleton);
    g_posedBend = g_bend;
    return moved;
}

void AimingOverhaul::Lean::OnPosed(uint8_t* skeleton) {
    const float bend = g_posedBend;
    if (skeleton != g_skeleton || bend == 0.0f) {
        return;
    }
    float pivot[3];
    std::copy_n(Skeleton::At(skeleton, g_spine).model + 12, 3, pivot);
    const uint32_t count = Skeleton::Count(skeleton);
    for (uint32_t i = g_spine; i < count; ++i) {
        const Skeleton::Bone bone = Skeleton::At(skeleton, i);
        if (!g_bends[i] || bone.held) {
            continue;
        }
        Turn(bone.model, bend);
        Turn(bone.model + 4, bend);
        Turn(bone.model + 8, bend);
        float* place = bone.model + 12;
        for (int k = 0; k < 3; ++k) {
            place[k] -= pivot[k];
        }
        Turn(place, bend);
        for (int k = 0; k < 3; ++k) {
            place[k] += pivot[k];
        }
    }
}

void AimingOverhaul::Lean::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
