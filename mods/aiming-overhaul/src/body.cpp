// The player's first-person body as posed each frame. See body.h.
#include "body.h"

#include "engine/aim.h"
#include "engine/pawn.h"
#include "engine/skeleton.h"
#include "fcse_api.h"
#include "lean.h"
#include "pitch_slide.h"
#include "ready.h"

#include <windows.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <initializer_list>

namespace {
    using AimingOverhaul::Body::Offset;
    using AimingOverhaul::Lean::Bend;
    namespace Skeleton = AimingOverhaul::Skeleton;

    // The bone the body bends at, everything above it bending with it; the one the view follows; and
    // where each arm starts, everything below it moving with the gun.
    constexpr uint32_t kSpine = FCSE::Crc32("Spine");
    constexpr uint32_t kCamera = FCSE::Crc32("Camera");
    constexpr uint32_t kLeftArm = FCSE::Crc32("L Clavicle");
    constexpr uint32_t kRightArm = FCSE::Crc32("R Clavicle");

    // Where the arms turn as they move the gun, in the view's axes, metres right, ahead and
    // up: between the shoulders, below and behind the eye.
    constexpr float kPivot[3] = {0.0f, -0.1f, -0.15f};

    // Milliseconds without a frame past which the player is not on foot, and nothing is posed.
    constexpr ULONGLONG kStaleMilliseconds = 200;

    // The skeleton followed, its spine and view bones, which bones the lean bends and which the
    // arms move; and the binding of the camera itself, of all that hangs from the view's bone.
    constexpr size_t kMostBones = 256;
    std::atomic<uint8_t*> g_skeleton{nullptr};
    int g_spine = -1;
    int g_camera = -1;
    std::array<bool, kMostBones> g_bends = {};
    std::array<bool, kMostBones> g_arms = {};
    std::atomic<uint8_t*> g_cameraBinding{nullptr};
    // What the last pose left in each bone. The animation writes most of the player's bones itself,
    // and one it has not written again since still holds it.
    std::array<std::array<float, 16>, kMostBones> g_written = {};

    // The body's pose this frame, two copies so a posing thread never reads one half written, and
    // when it was last followed.
    struct Pose {
        Bend bend;
        Offset arms;

        bool operator==(const Pose&) const = default;
    };
    Pose g_poses[2] = {};
    std::atomic<int> g_current{0};
    std::atomic<ULONGLONG> g_followed{0};

    bool StillWritten(uint32_t i, const float* model) {
        return std::equal(model, model + 16, g_written[i].begin());
    }

    // Marks the bones `tops` and every one below them in the tree as `set`'s, and no others; bones
    // come after their parents.
    void MarkBelow(uint8_t* skeleton, std::initializer_list<int> tops,
                   std::array<bool, kMostBones>& set) {
        const uint32_t count = Skeleton::Count(skeleton);
        set.fill(false);
        for (uint32_t i = 0; i < count; ++i) {
            const int parent = Skeleton::At(skeleton, i).parent;
            set[i] = std::find(tops.begin(), tops.end(), static_cast<int>(i)) != tops.end() ||
                     (parent >= 0 && static_cast<uint32_t>(parent) < i && set[parent]);
        }
    }

    // The bones the lean bends, the spine's and every one below it but the view's: the
    // first-person body is drawn lined up on the view's bone, so bending that too would take the bend
    // back off everything else; the view rolls by itself instead. And the arms.
    bool FindBones(uint8_t* skeleton) {
        g_spine = Skeleton::Find(skeleton, kSpine);
        g_camera = Skeleton::Find(skeleton, kCamera);
        if (g_spine < 0 || g_camera < 0 || Skeleton::Count(skeleton) > kMostBones) {
            return false;
        }
        MarkBelow(skeleton, {g_spine}, g_bends);
        MarkBelow(skeleton, {Skeleton::Find(skeleton, kLeftArm), Skeleton::Find(skeleton, kRightArm)},
                  g_arms);
        g_bends[g_camera] = g_arms[g_camera] = false;
        FCSE::Logf("body: %zd bones lean, %zd move with the gun",
                   std::count(g_bends.begin(), g_bends.end(), true),
                   std::count(g_arms.begin(), g_arms.end(), true));
        return true;
    }

    // The arms' offset as a move in the body's axes about the view, `camera` its posed matrix.
    class ArmsMove {
    public:
        ArmsMove(const float* camera, const Offset& offset) {
            const float cy = std::cos(offset.yaw), sy = std::sin(offset.yaw);
            const float cp = std::cos(offset.pitch), sp = std::sin(offset.pitch);
            const float cr = std::cos(offset.roll), sr = std::sin(offset.roll);
            // In the view's axes: rolled right about ahead, then pitched up about right, then turned
            // left about up.
            const float turn[3][3] = {
                {cy * cr - sy * sp * sr, -sy * cp, cy * sr + sy * sp * cr},
                {sy * cr + cy * sp * sr, cy * cp, sy * sr - cy * sp * cr},
                {-cp * sr, sp, cp * cr},
            };
            // The same turn in the body's axes, the view's axes being the camera's rows.
            const float shift[3] = {offset.right, offset.ahead, offset.up};
            for (int k = 0; k < 3; ++k) {
                m_pivot[k] = camera[12 + k];
                m_shift[k] = 0.0f;
                for (int j = 0; j < 3; ++j) {
                    m_pivot[k] += kPivot[j] * camera[4 * j + k];
                    m_shift[k] += shift[j] * camera[4 * j + k];
                    m_turn[k][j] = 0.0f;
                    for (int a = 0; a < 3; ++a) {
                        for (int b = 0; b < 3; ++b) {
                            m_turn[k][j] += camera[4 * a + k] * turn[a][b] * camera[4 * b + j];
                        }
                    }
                }
            }
        }

        void Apply(float* model) const {
            Turn(model);
            Turn(model + 4);
            Turn(model + 8);
            float* place = model + 12;
            for (int k = 0; k < 3; ++k) {
                place[k] -= m_pivot[k];
            }
            Turn(place);
            for (int k = 0; k < 3; ++k) {
                place[k] += m_pivot[k] + m_shift[k];
            }
        }

    private:
        void Turn(float* v) const {
            const float was[3] = {v[0], v[1], v[2]};
            for (int k = 0; k < 3; ++k) {
                v[k] = m_turn[k][0] * was[0] + m_turn[k][1] * was[1] + m_turn[k][2] * was[2];
            }
        }

        float m_turn[3][3];
        float m_pivot[3];
        float m_shift[3];
    };

    // The pose to apply to the player's skeleton: the lean's bend about the spine's unbent place,
    // and the arms' move about the view's bone as bent. False when there is none to apply.
    struct Applied {
        Pose pose;
        float pivot[3];
        float camera[16];
    };

    bool Prepare(uint8_t* skeleton, Applied& out) {
        if (GetTickCount64() - g_followed > kStaleMilliseconds) {
            return false;
        }
        out.pose = g_poses[g_current];
        if (out.pose == Pose{}) {
            return false;
        }
        std::copy_n(Skeleton::At(skeleton, g_camera).model, 16, out.camera);
        if (out.pose.bend.angle != 0.0f) {
            // The spine's place before the bend, which turns it about that place but slides it.
            const float* spine = Skeleton::At(skeleton, g_spine).model;
            std::copy_n(spine + 12, 3, out.pivot);
            if (StillWritten(g_spine, spine)) {
                out.pivot[0] -= AimingOverhaul::Lean::SlideFor(out.pose.bend.angle);
            }
            AimingOverhaul::Lean::Apply(out.camera, out.pivot, out.pose.bend);
        }
        return true;
    }
}

void AimingOverhaul::Body::Follow(uint8_t* pawn) {
    const Pose pose = {Lean::Posed(), Ready::Posed() + PitchSlide::Posed()};
    uint8_t* skeleton = Skeleton::Of(pawn, !(pose == g_poses[g_current]));
    if (skeleton != g_skeleton) {
        g_skeleton = nullptr;
        if (skeleton == nullptr || !FindBones(skeleton)) {
            return;
        }
        g_skeleton = skeleton;
    }
    const int next = 1 - g_current;
    g_poses[next] = pose;
    g_current = next;
    g_followed = GetTickCount64();
    g_cameraBinding = Aim::CameraBinding();
}

bool AimingOverhaul::Body::Found() {
    return g_skeleton != nullptr;
}

void AimingOverhaul::Body::OnPosed(uint8_t* skeleton) {
    Applied applied;
    if (skeleton != g_skeleton || !Prepare(skeleton, applied)) {
        return;
    }
    const Pose& pose = applied.pose;
    const bool bent = pose.bend.angle != 0.0f;
    const bool moved = !(pose.arms == Offset{});
    const ArmsMove move(applied.camera, pose.arms);
    const uint32_t count = Skeleton::Count(skeleton);
    for (uint32_t i = g_spine; i < count; ++i) {
        const bool bends = bent && g_bends[i];
        const bool moves = moved && g_arms[i];
        float* model = Skeleton::At(skeleton, i).model;
        if ((!bends && !moves) || StillWritten(i, model)) {
            continue;
        }
        if (bends) {
            Lean::Apply(model, applied.pivot, pose.bend);
        }
        if (moves) {
            move.Apply(model);
        }
        std::copy_n(model, 16, g_written[i].begin());
    }
}

bool AimingOverhaul::Body::OnAttached(uint8_t* skeleton, int bone, uint8_t* child, float* model) {
    Applied applied;
    if (skeleton != g_skeleton || bone != g_camera || !Prepare(skeleton, applied)) {
        return false;
    }
    const Pose& pose = applied.pose;
    const bool bent = pose.bend.angle != 0.0f;
    const bool moved = child != g_cameraBinding && !(pose.arms == Offset{});
    if (bent) {
        Lean::Apply(model, applied.pivot, pose.bend);
    }
    if (moved) {
        ArmsMove(applied.camera, pose.arms).Apply(model);
    }
    return bent || moved;
}

void AimingOverhaul::Body::MoveShot(uint8_t* pawn, float origin[3]) {
    uint8_t* skeleton = g_skeleton;
    Applied applied;
    if (skeleton == nullptr || !Prepare(skeleton, applied) || applied.pose.bend.angle == 0.0f) {
        return;
    }
    const float* view = Skeleton::At(skeleton, g_camera).model;
    const float shift[3] = {applied.camera[12] - view[12], applied.camera[13] - view[13],
                            applied.camera[14] - view[14]};
    float moved[3];
    if (Pawn::ToWorld(pawn, shift, moved)) {
        for (int k = 0; k < 3; ++k) {
            origin[k] += moved[k];
        }
    }
}
