// The player's legs in first person. See legs.h.
#include "legs.h"

#include "engine/graphic.h"
#include "engine/memory.h"
#include "engine/pawn.h"
#include "engine/skeleton.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <numbers>

namespace {
    using PlayerBody::Field;
    namespace Graphic = PlayerBody::Graphic;
    namespace Pawn = PlayerBody::Pawn;
    namespace Skeleton = PlayerBody::Skeleton;

    struct V3 {
        float x, y, z;

        float operator[](int i) const { return i == 0 ? x : i == 1 ? y : z; }
    };
    V3 operator+(V3 a, V3 b) { return {a.x + b.x, a.y + b.y, a.z + b.z}; }
    V3 operator-(V3 a, V3 b) { return {a.x - b.x, a.y - b.y, a.z - b.z}; }
    V3 operator*(V3 a, float s) { return {a.x * s, a.y * s, a.z * s}; }
    float Dot(V3 a, V3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
    V3 Cross(V3 a, V3 b) { return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x}; }
    float Length(V3 a) { return std::sqrt(Dot(a, a)); }
    V3 Normal(V3 a) {
        const float length = Length(a);
        return length > 1e-6f ? a * (1.0f / length) : V3{0.0f, 0.0f, 0.0f};
    }

    float Clamp01(float t) { return (std::clamp)(t, 0.0f, 1.0f); }
    float Smooth(float t) {
        t = Clamp01(t);
        return t * t * (3.0f - 2.0f * t);
    }
    // `value` eased toward `target`, `time` seconds being most of the way.
    float Ease(float value, float target, float seconds, float time) {
        return value + (target - value) * (1.0f - std::exp(-seconds / time));
    }

    // A 4x4 in the engine's layout: three axis rows, then the place.
    struct Matrix {
        float m[16];

        V3 Row(int i) const { return {m[4 * i], m[4 * i + 1], m[4 * i + 2]}; }
        void SetRow(int i, V3 v) {
            m[4 * i] = v.x;
            m[4 * i + 1] = v.y;
            m[4 * i + 2] = v.z;
            m[4 * i + 3] = i == 3 ? 1.0f : 0.0f;
        }
    };

    // The inverse of an affine 4x4, false where it is not one.
    bool Invert(const float* m, Matrix& out) {
        if (std::fabs(m[3]) + std::fabs(m[7]) + std::fabs(m[11]) > 1e-3f) {
            return false;
        }
        const float det = m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) +
                          m[2] * (m[4] * m[9] - m[5] * m[8]);
        if (std::fabs(det) < 1e-8f) {
            return false;
        }
        const float k = 1.0f / det;
        const float a[9] = {
            (m[5] * m[10] - m[6] * m[9]) * k, (m[2] * m[9] - m[1] * m[10]) * k, (m[1] * m[6] - m[2] * m[5]) * k,
            (m[6] * m[8] - m[4] * m[10]) * k, (m[0] * m[10] - m[2] * m[8]) * k, (m[2] * m[4] - m[0] * m[6]) * k,
            (m[4] * m[9] - m[5] * m[8]) * k,  (m[1] * m[8] - m[0] * m[9]) * k,  (m[0] * m[5] - m[1] * m[4]) * k,
        };
        for (int i = 0; i < 3; ++i) {
            out.SetRow(i, {a[3 * i], a[3 * i + 1], a[3 * i + 2]});
        }
        const V3 t = {m[12], m[13], m[14]};
        out.SetRow(3, {-(t.x * a[0] + t.y * a[3] + t.z * a[6]), -(t.x * a[1] + t.y * a[4] + t.z * a[7]),
                       -(t.x * a[2] + t.y * a[5] + t.z * a[8])});
        return true;
    }

    // A turn as three rows: v turned is v.x r[0] + v.y r[1] + v.z r[2].
    struct Turn {
        V3 r[3];
    };
    constexpr Turn kNoTurn = {{{1.0f, 0.0f, 0.0f}, {0.0f, 1.0f, 0.0f}, {0.0f, 0.0f, 1.0f}}};

    V3 Apply(V3 v, const Turn& turn) {
        return turn.r[0] * v.x + turn.r[1] * v.y + turn.r[2] * v.z;
    }

    // Axes along `along`, then toward `pole` square to it, then the hinge square to both.
    void Axes(V3 along, V3 pole, V3 out[3]) {
        out[0] = Normal(along);
        out[1] = Normal(pole - out[0] * Dot(pole, out[0]));
        out[2] = Cross(out[0], out[1]);
    }

    // The turn taking a limb along `from`, bending toward `fromPole`, to one along `to` bending
    // toward `toPole`.
    Turn Between(V3 from, V3 fromPole, V3 to, V3 toPole) {
        V3 f[3];
        V3 t[3];
        Axes(from, fromPole, f);
        Axes(to, toPole, t);
        Turn turn;
        for (int i = 0; i < 3; ++i) {
            turn.r[i] = t[0] * f[0][i] + t[1] * f[1][i] + t[2] * f[2][i];
        }
        return turn;
    }

    // A turn about the body's up, by `angle` radians from ahead toward the right.
    Turn AboutUp(float angle) {
        const float c = std::cos(angle);
        const float s = std::sin(angle);
        return {{{c, -s, 0.0f}, {s, c, 0.0f}, {0.0f, 0.0f, 1.0f}}};
    }

    // `bind` turned about its joint at `from` and the joint carried to `to`.
    Matrix Moved(const Matrix& bind, const Turn& turn, V3 from, V3 to) {
        Matrix out;
        for (int i = 0; i < 3; ++i) {
            out.SetRow(i, Apply(bind.Row(i), turn));
        }
        out.SetRow(3, Apply(bind.Row(3) - from, turn) + to);
        return out;
    }

    constexpr uint32_t kPelvis = FCSE::Crc32("Pelvis");
    constexpr uint32_t kSpine = FCSE::Crc32("Spine");

    // A leg's bones by name, left then right.
    struct LegNames {
        uint32_t thigh, calf, foot, toe, knee;
    };
    constexpr LegNames kLegNames[] = {
        {FCSE::Crc32("L Thigh"), FCSE::Crc32("L Calf"), FCSE::Crc32("L Foot"), FCSE::Crc32("L Toe"),
         FCSE::Crc32("L Knee")},
        {FCSE::Crc32("R Thigh"), FCSE::Crc32("R Calf"), FCSE::Crc32("R Foot"), FCSE::Crc32("R Toe"),
         FCSE::Crc32("R Knee")},
    };

    // The mesh's axes into the body's: the mesh faces along its x and has its left along y.
    constexpr Turn kMeshToBody = {{{0.0f, 1.0f, 0.0f}, {-1.0f, 0.0f, 0.0f}, {0.0f, 0.0f, 1.0f}}};
    constexpr V3 kAhead = {0.0f, 1.0f, 0.0f};
    // How high the mesh's hips stand above the ground, its root node's height.
    constexpr float kStandingHips = 0.995f;
    // How much of the way the feet follow hips the animation swings back to look down.
    constexpr float kFeetFollow = 0.5f;
    // How far the knees point out, as a share of ahead: standing, and more at the deepest crouch,
    // so an eye the animation lowers between them sees past them.
    constexpr float kKneesOut = 0.15f;
    constexpr float kCrouchKneesOut = 0.9f;
    // How far apart the feet go at the deepest crouch, and how far the hips drop to it.
    constexpr float kCrouchStance = 0.10f;
    constexpr float kDeepestCrouch = 0.6f;
    // How far the toes turn with the knees.
    constexpr float kToesFollow = 0.7f;

    // A leg's bones, their rest matrices in the body's axes with the hips at nought, its thigh's and
    // shin's lengths, and its side, one to the right. A toe or knee bone left unskinned is not posed.
    struct Leg {
        int thigh, calf, foot, toe, knee;
        Matrix restThigh, restCalf, restFoot, restToe, restKnee;
        float upper, lower;
        float side;
    };
    // The hips hang below the spine, which the first-person animation stands upright.
    struct Rig {
        uint8_t* skeleton;
        int pelvis;
        int spine;
        Matrix restPelvis, restSpine;
        Leg legs[2];
    };
    Rig g_rig = {};
    uint8_t* g_refused = nullptr;

    // A bone's rest matrix in the full body's mesh, false where the mesh skins nothing to it.
    bool Rest(const Graphic::Skin& skin, int bone, Matrix& out) {
        const int32_t* end = skin.bones + skin.count;
        const int32_t* slot = std::find(skin.bones, end, bone);
        return bone >= 0 && slot != end && Invert(skin.inverseBinds + 16 * (slot - skin.bones), out);
    }

    bool Load(uint8_t* graphic, uint8_t* skeleton) {
        Graphic::Skin skin;
        Rig rig = {skeleton, Skeleton::Find(skeleton, kPelvis), Skeleton::Find(skeleton, kSpine)};
        if (!Graphic::SkinOf(graphic, Graphic::kFullBody, skin) || !Rest(skin, rig.pelvis, rig.restPelvis)) {
            return false;
        }
        // Each rest matrix out of the mesh's axes into the body's, the hips at nought.
        const V3 hips = rig.restPelvis.Row(3);
        const auto rest = [&](int bone, Matrix& out) {
            if (!Rest(skin, bone, out)) {
                return false;
            }
            out = Moved(out, kMeshToBody, hips, {0.0f, 0.0f, 0.0f});
            return true;
        };
        rig.restPelvis = Moved(rig.restPelvis, kMeshToBody, hips, {0.0f, 0.0f, 0.0f});
        if (!rest(rig.spine, rig.restSpine)) {
            return false;
        }
        for (int s = 0; s < 2; ++s) {
            Leg& leg = rig.legs[s];
            leg.thigh = Skeleton::Find(skeleton, kLegNames[s].thigh);
            leg.calf = Skeleton::Find(skeleton, kLegNames[s].calf);
            leg.foot = Skeleton::Find(skeleton, kLegNames[s].foot);
            leg.toe = Skeleton::Find(skeleton, kLegNames[s].toe);
            leg.knee = Skeleton::Find(skeleton, kLegNames[s].knee);
            if (!rest(leg.thigh, leg.restThigh) || !rest(leg.calf, leg.restCalf) ||
                !rest(leg.foot, leg.restFoot)) {
                return false;
            }
            if (!rest(leg.toe, leg.restToe)) {
                leg.toe = -1;
            }
            if (!rest(leg.knee, leg.restKnee)) {
                leg.knee = -1;
            }
            const V3 hip = leg.restThigh.Row(3);
            leg.upper = Length(leg.restCalf.Row(3) - hip);
            leg.lower = Length(leg.restFoot.Row(3) - leg.restCalf.Row(3));
            leg.side = hip.x > 0.0f ? 1.0f : -1.0f;
        }
        FCSE::Logf("legs: posed on %u bones, thigh %.3f m and shin %.3f m", Skeleton::Count(skeleton),
                   rig.legs[0].upper, rig.legs[0].lower);
        g_rig = rig;
        return true;
    }

    // How the player moves, in the body's axes: metres a second right and ahead and in all, the way
    // the feet travel, a stride's length and how far through it, and how much the legs walk rather
    // than stand and are tucked up off the ground.
    struct Motion {
        bool placed;
        V3 place;
        float right, ahead, speed;
        V3 direction;
        float stride, phase, walking, tucked;
    };
    Motion g_motion = {};

    // A stride's length and its share on the ground grow from a walk to a run; each foot is set
    // down ahead, carried back under the hips, and swung forward lifted.
    constexpr float kStrideAtRest = 1.0f;
    constexpr float kStridePerSpeed = 0.4f;
    constexpr float kMostReach = 0.42f;
    constexpr float kWalkGround = 0.62f;
    constexpr float kRunGround = 0.38f;
    constexpr float kWalkLift = 0.10f;
    constexpr float kRunLift = 0.22f;
    // Metres a second from which the legs fully walk; and how far the feet come up off the ground.
    constexpr float kWalkingFrom = 0.8f;
    constexpr V3 kTuck = {0.0f, -0.10f, 0.30f};
    constexpr float kTuckAbove = 0.35f;
    // Seconds the speed and the blends settle over, and metres in a frame past which the player was
    // moved rather than moved himself.
    constexpr float kSpeedSettles = 0.08f;
    constexpr float kBlendSettles = 0.12f;
    constexpr float kTeleport = 3.0f;

    void Follow(uint8_t* pawn, float seconds) {
        uint8_t* entity = Pawn::Entity(pawn);
        if (entity == nullptr) {
            return;
        }
        const float* m = &Field<float>(entity, Pawn::kEntityMatrix);
        const V3 place = {m[12], m[13], m[14]};
        const V3 moved = place - g_motion.place;
        float right = 0.0f;
        float ahead = 0.0f;
        if (g_motion.placed && seconds > 0.0f && Length(moved) < kTeleport) {
            right = Dot(moved, {m[0], m[1], m[2]}) / seconds;
            ahead = Dot(moved, {m[4], m[5], m[6]}) / seconds;
        }
        g_motion.placed = true;
        g_motion.place = place;
        g_motion.right = Ease(g_motion.right, right, seconds, kSpeedSettles);
        g_motion.ahead = Ease(g_motion.ahead, ahead, seconds, kSpeedSettles);
        const float speed = std::hypot(g_motion.right, g_motion.ahead);
        if (speed > 0.1f) {
            g_motion.direction = {g_motion.right / speed, g_motion.ahead / speed, 0.0f};
        }
        g_motion.speed = speed;
        g_motion.stride = kStrideAtRest + kStridePerSpeed * speed;
        g_motion.phase = std::fmod(g_motion.phase + speed * seconds / g_motion.stride, 1.0f);
        g_motion.walking = Ease(g_motion.walking, Smooth(speed / kWalkingFrom), seconds, kBlendSettles);
        const bool airborne = Pawn::Jumping(pawn) || Pawn::AboveGround(pawn) > kTuckAbove;
        g_motion.tucked = Ease(g_motion.tucked, airborne ? 1.0f : 0.0f, seconds, kBlendSettles);
    }

    // Where a foot is off its place at rest, `phase` through its stride.
    V3 Step(float phase) {
        const float run = Smooth((g_motion.speed - 2.0f) / 3.0f);
        const float ground = std::lerp(kWalkGround, kRunGround, run);
        const float reach = (std::min)(ground * g_motion.stride * 0.5f, kMostReach);
        float along;
        float up = 0.0f;
        if (phase < ground) {
            along = reach - 2.0f * reach * phase / ground;
        } else {
            const float t = (phase - ground) / (1.0f - ground);
            along = -reach + 2.0f * reach * Smooth(t);
            up = std::lerp(kWalkLift, kRunLift, run) * std::sin(std::numbers::pi_v<float> * t);
        }
        return g_motion.direction * along + V3{0.0f, 0.0f, up};
    }

    // The bones written, the hips and five a leg, and their posed matrices; two copies, so a posing
    // thread never reads one half written.
    using Clock = std::chrono::steady_clock;
    constexpr size_t kMostPosed = 11;
    struct Pose {
        uint8_t* skeleton;
        Clock::time_point made;
        uint32_t count;
        int bones[kMostPosed];
        Matrix matrices[kMostPosed];

        void Add(int bone, const Matrix& matrix) {
            if (bone >= 0) {
                bones[count] = bone;
                matrices[count++] = matrix;
            }
        }
    };
    Pose g_poses[2] = {};
    std::atomic<int> g_current{-1};
    // How long without a pose before the legs are left to the animation.
    constexpr auto kStale = std::chrono::milliseconds(200);

    // Where the hips are: below the spine as the animation holds it, lowered by a crouch and swung
    // back to look down.
    V3 Hips(uint8_t* skeleton) {
        const float* spine = Skeleton::Posed(skeleton, g_rig.spine);
        return V3{spine[12], spine[13], spine[14]} - g_rig.restSpine.Row(3);
    }

    void Build(uint8_t* skeleton, Pose& pose) {
        const Rig& rig = g_rig;
        const V3 hips = Hips(skeleton);
        const float crouch = Clamp01((kStandingHips - hips.z) / kDeepestCrouch);
        pose = {skeleton, Clock::now()};
        pose.Add(rig.pelvis, Moved(rig.restPelvis, kNoTurn, {0.0f, 0.0f, 0.0f}, hips));
        for (int s = 0; s < 2; ++s) {
            const Leg& leg = rig.legs[s];
            const V3 hip0 = leg.restThigh.Row(3);
            const V3 knee0 = leg.restCalf.Row(3);
            const V3 ankle0 = leg.restFoot.Row(3);
            const V3 hip = hip0 + hips;
            const float phase = std::fmod(g_motion.phase + 0.5f * s, 1.0f);
            // The foot on the ground, as high as it stands at rest, partway under hips swung back.
            V3 ankle = V3{ankle0.x + leg.side * kCrouchStance * crouch, ankle0.y + hips.y * kFeetFollow,
                          ankle0.z + kStandingHips} +
                       Step(phase) * g_motion.walking + kTuck * g_motion.tucked;
            const V3 pole = Normal({leg.side * (kKneesOut + kCrouchKneesOut * crouch), 1.0f, 0.0f});
            // A foot out of reach is lifted toward the hip with the leg straight.
            const float most = (leg.upper + leg.lower) * 0.999f;
            float reach = Length(ankle - hip);
            if (reach > most) {
                ankle = hip + (ankle - hip) * (most / reach);
                reach = most;
            }
            reach = (std::max)(reach, 0.05f);
            V3 plane[3];
            Axes(ankle - hip, pole, plane);
            const float bend = (std::clamp)((leg.upper * leg.upper + reach * reach - leg.lower * leg.lower) /
                                                (2.0f * leg.upper * reach),
                                            -1.0f, 1.0f);
            const V3 knee = hip + (plane[0] * bend + plane[1] * std::sqrt(1.0f - bend * bend)) * leg.upper;

            // The knee cap, ahead at rest, turned to where the knee bends, and the toes after it.
            const Turn thigh = Between(knee0 - hip0, kAhead, knee - hip, pole);
            const Turn shin = Between(ankle0 - knee0, kAhead, ankle - knee, pole);
            const Turn foot = AboutUp(std::atan2(pole.x, pole.y) * kToesFollow);
            pose.Add(leg.thigh, Moved(leg.restThigh, thigh, hip0, hip));
            pose.Add(leg.knee, Moved(leg.restKnee, thigh, hip0, hip));
            pose.Add(leg.calf, Moved(leg.restCalf, shin, knee0, knee));
            pose.Add(leg.foot, Moved(leg.restFoot, foot, ankle0, ankle));
            pose.Add(leg.toe, Moved(leg.restToe, foot, ankle0, ankle));
        }
    }
}

void PlayerBody::Legs::Update(uint8_t* pawn, float seconds, bool shown) {
    uint8_t* graphic = shown ? Pawn::Graphic(pawn) : nullptr;
    uint8_t* skeleton = graphic != nullptr ? Graphic::Skeleton(graphic, true) : nullptr;
    if (skeleton == nullptr) {
        g_current = -1;
        g_motion.placed = false;
        return;
    }
    if (skeleton != g_rig.skeleton && !Load(graphic, skeleton)) {
        if (g_refused != skeleton) {
            FCSE::Logf("legs: the full body's rest pose could not be read, so the legs are not posed");
            g_refused = skeleton;
        }
        g_current = -1;
        return;
    }
    Follow(pawn, seconds);
    const int next = g_current == 0 ? 1 : 0;
    Build(skeleton, g_poses[next]);
    g_current = next;
}

void PlayerBody::Legs::OnPosed(uint8_t* skeleton) {
    const int current = g_current;
    if (current < 0) {
        return;
    }
    const Pose& pose = g_poses[current];
    if (skeleton != pose.skeleton || Clock::now() - pose.made > kStale) {
        return;
    }
    for (uint32_t i = 0; i < pose.count; ++i) {
        std::copy_n(pose.matrices[i].m, 16, Skeleton::Posed(skeleton, pose.bones[i]));
    }
}
