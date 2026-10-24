// Leaning: held Q or E slides the upper body out to the side and bends it from the belly, and the
// head, the view, the arms and the gun go with it; see
// docs/docs/engine-internals/first-person-aiming.md#leaning.
#include "lean.h"

#include "ease.h"
#include "engine/pawn.h"
#include "engine/skeleton.h"
#include "fcse_api.h"

#include <windows.h>

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

    // Radians and metres to the side at full lean, the upper body sliding over the hips as it
    // bends, and seconds for the lean to follow, which takes it most of the way in a quarter of a
    // second.
    constexpr float kMostBend = 0.26f;
    constexpr float kMostSlide = 0.10f;
    constexpr float kFollow = 0.08f;

    // A lean key let go within this many milliseconds was tapped, and the lean stays until the next
    // tap; held longer, the lean ends with it.
    constexpr ULONGLONG kTapMilliseconds = 250;

    // The side leaned to, -1 left, 1 right, 0 none; whether the lean outlasts its key, tapped; the
    // side whose press just ended a tapped lean, whose release is not a tap; and when the last lean
    // began.
    int g_side = 0;
    bool g_latched = false;
    int g_ended = 0;
    ULONGLONG g_pressed = 0;
    float g_bend = 0.0f;

    void Press(int side) {
        if (g_side == side && g_latched) {
            g_side = 0;
            g_latched = false;
            g_ended = side;
            return;
        }
        g_side = side;
        g_latched = false;
        g_pressed = GetTickCount64();
    }

    void Release(int side) {
        if (g_ended == side) {
            g_ended = 0;
        } else if (g_side == side) {
            g_latched = GetTickCount64() - g_pressed < kTapMilliseconds;
            if (!g_latched) {
                g_side = 0;
            }
        }
    }

    // The skeleton leaned, its bend's bone and the view's, and which bones bend; and the bend that
    // posing it applies, in radians, right positive, about the look's direction, which is pitched
    // up by the second, in radians.
    constexpr size_t kMostBones = 256;
    std::atomic<uint8_t*> g_skeleton{nullptr};
    int g_spine = -1;
    int g_camera = -1;
    std::array<bool, kMostBones> g_bends = {};
    std::atomic<float> g_posedBend{0.0f};
    std::atomic<float> g_posedPitch{0.0f};
    // What the last bend left in each bone. The animation writes most of the player's bones itself,
    // and one it has not written again since is still bent.
    std::array<std::array<float, 16>, kMostBones> g_bent = {};

    // How far the upper body slides to the side, in metres, right positive, as it bends by `bend`.
    float SlideFor(float bend) {
        return bend / kMostBend * kMostSlide;
    }

    // Turns the point or axis `v`, in the body's axes, by `bend` about the look's direction, ahead
    // pitched up by `pitch`. The view rolls about that same direction, so whatever hangs from it
    // stays lined up on it.
    void Turn(float* v, float bend, float pitch) {
        const float u[3] = {0.0f, std::cos(pitch), std::sin(pitch)};
        const float c = std::cos(bend);
        const float s = std::sin(bend);
        const float along = (u[0] * v[0] + u[1] * v[1] + u[2] * v[2]) * (1.0f - c);
        const float cross[3] = {u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2],
                                u[0] * v[1] - u[1] * v[0]};
        for (int k = 0; k < 3; ++k) {
            v[k] = v[k] * c + cross[k] * s + u[k] * along;
        }
    }

    // Bends a posed matrix by `bend` about `pivot`, the spine's unbent place, and slides it.
    void Bend(float* model, const float pivot[3], float bend, float pitch) {
        Turn(model, bend, pitch);
        Turn(model + 4, bend, pitch);
        Turn(model + 8, bend, pitch);
        float* place = model + 12;
        for (int k = 0; k < 3; ++k) {
            place[k] -= pivot[k];
        }
        Turn(place, bend, pitch);
        for (int k = 0; k < 3; ++k) {
            place[k] += pivot[k];
        }
        place[0] += SlideFor(bend);
    }

    // Whether bone `i` still holds what the last bend left in it, the animation not having written
    // it since.
    bool StillBent(uint32_t i, const float* model) {
        return std::equal(model, model + 16, g_bent[i].begin());
    }

    // The spine's place before the bend, which turns it about that place but slides it.
    void UnbentPivot(uint8_t* skeleton, float bend, float pivot[3]) {
        const float* spine = AimingOverhaul::Skeleton::At(skeleton, g_spine).model;
        std::copy_n(spine + 12, 3, pivot);
        if (StillBent(g_spine, spine)) {
            pivot[0] -= SlideFor(bend);
        }
    }

    // Notes which of the skeleton's bones bend: the spine's and every one below it in the tree but
    // the view's. The first-person body is drawn lined up on the view's bone, so bending that too
    // would take the bend back off everything else; the view rolls by itself instead.
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
        g_bends[g_camera] = false;
        return true;
    }

}

void AimingOverhaul::Lean::OnSignal(uint32_t signal) {
    if (signal == kStartLeft) {
        Press(-1);
    } else if (signal == kStopLeft) {
        Release(-1);
    } else if (signal == kStartRight) {
        Press(1);
    } else if (signal == kStopRight) {
        Release(1);
    }
}

float AimingOverhaul::Lean::Update(uint8_t* pawn, float seconds, bool free) {
    // Sprinting ends a tapped lean, rather than leaving it to come back after.
    if (g_latched && !free) {
        g_side = 0;
        g_latched = false;
    }
    float target = 0.0f;
    if (free) {
        target = kMostBend * static_cast<float>(g_side);
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
            return 0.0f;
        }
        g_skeleton = skeleton;
    }
    // The view rolls with the bend the skeleton was last posed with, which is what will be drawn.
    const float bend = g_posedBend;
    g_posedBend = g_bend;
    g_posedPitch = Pawn::Pitch(pawn);
    return bend;
}

void AimingOverhaul::Lean::OnPosed(uint8_t* skeleton) {
    const float bend = g_posedBend;
    const float pitch = g_posedPitch;
    if (skeleton != g_skeleton || bend == 0.0f) {
        return;
    }
    float pivot[3];
    UnbentPivot(skeleton, bend, pivot);
    const uint32_t count = Skeleton::Count(skeleton);
    for (uint32_t i = g_spine; i < count; ++i) {
        float* model = Skeleton::At(skeleton, i).model;
        if (g_bends[i] && !StillBent(i, model)) {
            Bend(model, pivot, bend, pitch);
            std::copy_n(model, 16, g_bent[i].begin());
        }
    }
}

bool AimingOverhaul::Lean::OnAttached(uint8_t* skeleton, int bone, float* model) {
    const float bend = g_posedBend;
    const float pitch = g_posedPitch;
    if (skeleton != g_skeleton || bone != g_camera || bend == 0.0f) {
        return false;
    }
    float pivot[3];
    UnbentPivot(skeleton, bend, pivot);
    Bend(model, pivot, bend, pitch);
    return true;
}
