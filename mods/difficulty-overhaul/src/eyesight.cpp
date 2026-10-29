// Eyesight: how soldiers see the player: a farther focus cone, a periphery that only notices a player
// moving upright, his body over cover counting double up close, and moving upright heard behind.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with one stack argument, which a free function spells __fastcall.
    using EvaluatorFn = float(__fastcall*)(void* evaluator, void* unused, bool* applies);
    using TargetDistanceFn = float(__fastcall*)(const void* context);

    // CSensorySystem::AdjustFOV past its range caps, once one soldier's cones for one target are set.
    FCSE::Relocation<uint8_t*> g_conesSet{FCSE::Pattern(
        "8B 45 10 8B 80 ?? ?? ?? ?? 50 E8 ?? ?? ?? ?? 83 C4 04 84 C0 74 ?? 8B 4E 04 F3 0F 10 05 ?? ?? ?? ?? "
        "F3 0F 11 41 0C")};
    // CDistanceEvaluator, CFOVEvaluator and CPawnSamplingEvaluator::ComputeVisibility, the last a
    // third for each of three points on the target's body a ray reaches; then
    // CVisibilityContext::GetTargetDistance.
    FCSE::Relocation<EvaluatorFn> g_distance{
        FCSE::Pattern("8B 44 24 04 83 EC 1C 56 8B F1 C6 00 01 8B 46 24 80 78 3E 00 57 0F 85")};
    FCSE::Relocation<EvaluatorFn> g_fov{FCSE::Uplay(0x00A21380)};
    FCSE::Relocation<EvaluatorFn> g_sampling{FCSE::Uplay(0x00A21D90)};
    FCSE::Relocation<TargetDistanceFn> g_targetDistance{FCSE::Uplay(0x00614F50)};
    EvaluatorFn g_originalDistance = nullptr;
    EvaluatorFn g_originalFov = nullptr;
    EvaluatorFn g_originalSampling = nullptr;

    // AdjustFOV's frame at the hook: whether its target is a player, and the target agent.
    constexpr ptrdiff_t kTargetIsPlayer = 0x0E;
    constexpr ptrdiff_t kTarget = 0x24;
    // A visibility evaluator's parameters and context; the parameters' full-visibility share of the
    // focus cone and visibility at its end; the context's cones, target, eye, target position and
    // view direction; each cone a length in metres then a full angle in degrees.
    constexpr ptrdiff_t kEvaluatorParameters = 0x04;
    constexpr ptrdiff_t kEvaluatorContext = 0x24;
    constexpr ptrdiff_t kFullVisibilityShare = 0x70;
    constexpr ptrdiff_t kVisibilityAtEnd = 0x74;
    constexpr ptrdiff_t kContextCones = 0x04;
    constexpr ptrdiff_t kContextTarget = 0x0C;
    constexpr ptrdiff_t kContextEye = 0x10;
    constexpr ptrdiff_t kContextTargetPosition = 0x1C;
    constexpr ptrdiff_t kContextView = 0x28;
    constexpr ptrdiff_t kFocusLength = 0x08;
    constexpr ptrdiff_t kFocusAngle = 0x0C;
    constexpr ptrdiff_t kPeripheralLength = 0x1C;
    constexpr ptrdiff_t kPeripheralAngle = 0x20;

    // Hardcore: the focus cone's reach against the game's, the angle both cones span, and the share
    // of the focus cone the player is fully visible within. FovDetour keeps the game's focus angle
    // for where he counts fully.
    constexpr float kFocusReach = 100.0f / 60.0f;
    constexpr float kPeripheralSpan = 180.0f;
    constexpr float kFullVisibility = 0.5f;
    // Hardcore: how visible the player is in the periphery, at the focus cone's edge and at the
    // peripheral cone's.
    constexpr float kPeripheralNearFocus = 0.55f;
    constexpr float kPeripheralAtEdge = 0.35f;
    // Hardcore: in the periphery a player standing still or moving crouched counts this share of one
    // moving upright, who counts in full from this speed, a walk.
    constexpr float kStillPeriphery = 0.2f;
    constexpr float kWalkSpeed = 2.0f;
    // Hardcore: a player moving upright at least this fast is noticed behind a soldier within the
    // first range, as visible as the second, and sensed in full at any angle within the third.
    constexpr float kMovingSpeed = 0.5f;
    constexpr float kHearingRange = 20.0f;
    constexpr float kHeardBehind = 0.5f;
    constexpr float kArmsLength = 2.0f;
    constexpr float kAllAround = 360.0f;
    // The two low stances: crouched, and the one swimming forces.
    constexpr int kCrouched = 1;
    constexpr int kLow = 2;
    // Hardcore: within this range a head over cover is as plain as a whole man's top half, so the
    // share of the player's body the rays reach counts this many times over, up to all of it.
    constexpr float kCloseRange = 40.0f;
    constexpr float kCloseSampling = 2.0f;
    // The game's cap on a cone against the player, unless he looks through a scope.
    constexpr float kMaxFocusLength = 150.0f;

    // The player's agent, the target AdjustFOV last flagged as a player.
    std::atomic<const void*> g_player{nullptr};
    // Half the focus cone's angle AdjustFOV gave the player on this thread, before both cones were
    // widened: within it he counts fully. Then how much of a moving player's visibility he keeps in
    // the periphery, and whether the player moves upright close enough to be heard behind him, and
    // to be sensed in full.
    thread_local float t_focusHalf = 0.0f;
    thread_local float t_motion = 1.0f;
    thread_local bool t_heard = false;
    thread_local bool t_close = false;

    bool ForPlayer(const uint8_t* context) {
        const void* player = g_player.load(std::memory_order_relaxed);
        return player != nullptr && Read<const void*>(context, kContextTarget) == player;
    }

    // Visibility by distance: full within `full` of the cone's `length`, then down to `atEnd` at it.
    float Falloff(float distance, float length, float full, float atEnd) {
        if (distance > length || full <= 0.0f) {
            return 0.0f;
        }
        const float fullTo = full * length;
        if (distance < fullTo) {
            return 1.0f;
        }
        return 1.0f - (1.0f - atEnd) * (distance - fullTo) / (length - fullTo);
    }

    // How much the player's movement shows, which only moving upright does, and how close to `eye` he
    // does so.
    void Motion(void* player, const float eye[3]) {
        void* pawn = Shooting::Pawn(player);
        const int stance = Shooting::Stance(pawn);
        float velocity[3];
        Shooting::Velocity(pawn, velocity);
        // Every way of moving upright counts, a jump on the spot included.
        const float speed = stance == kCrouched || stance == kLow
                                ? 0.0f
                                : std::hypot(velocity[0], velocity[1], velocity[2]);
        t_motion = kStillPeriphery + (1.0f - kStillPeriphery) * std::min(speed / kWalkSpeed, 1.0f);

        float at[3] = {};
        void* entity = Game::EntityOf(pawn);
        if (entity != nullptr) {
            Game::Position(entity, at);
        }
        const float distance = std::hypot(at[0] - eye[0], at[1] - eye[1]);
        const bool moving = entity != nullptr && speed >= kMovingSpeed;
        t_heard = moving && distance <= kHearingRange;
        t_close = moving && distance <= kArmsLength;
    }

    void OnConesSet(FCSE_MidHookContext* ctx) {
        const auto* frame = reinterpret_cast<const uint8_t*>(ctx->esp);
        if (Read<uint8_t>(frame, kTargetIsPlayer) == 0) {
            return;
        }
        void* player = Read<void*>(frame, kTarget);
        g_player.store(player, std::memory_order_relaxed);
        const auto* context = reinterpret_cast<const uint8_t*>(ctx->esi);
        Motion(player, Vec3(context, kContextEye));
        auto* cones = Read<uint8_t*>(context, kContextCones);
        float focus = Read<float>(cones, kFocusLength);
        if (focus < kMaxFocusLength) {
            focus = std::min(focus * kFocusReach, kMaxFocusLength);
            Write(cones, kFocusLength, focus);
        }
        Write(cones, kPeripheralLength, std::max(Read<float>(cones, kPeripheralLength), focus));
        const float span = std::max(Read<float>(cones, kPeripheralAngle),
                                    t_heard ? kAllAround : kPeripheralSpan);
        t_focusHalf = Read<float>(cones, kFocusAngle) / 2.0f;
        Write(cones, kPeripheralAngle, span);
        Write(cones, kFocusAngle, std::max(Read<float>(cones, kFocusAngle), span));
    }

    float __fastcall DistanceDetour(void* evaluator, void* unused, bool* applies) {
        const float game = g_originalDistance(evaluator, unused, applies);
        const auto* context = Read<const uint8_t*>(evaluator, kEvaluatorContext);
        if (!ForPlayer(context)) {
            return game;
        }
        const auto* parameters = Read<const uint8_t*>(evaluator, kEvaluatorParameters);
        const float length = Read<float>(Read<const uint8_t*>(context, kContextCones), kFocusLength);
        const float distance = g_targetDistance(context);
        const float atEnd = Read<float>(parameters, kVisibilityAtEnd);
        // Any other result is the game's case for a player with his back turned, left as it is.
        if (std::abs(game - Falloff(distance, length, Read<float>(parameters, kFullVisibilityShare),
                                    atEnd)) > 1.0e-3f) {
            return game;
        }
        return Falloff(distance, length, kFullVisibility, atEnd);
    }

    float __fastcall FovDetour(void* evaluator, void* unused, bool* applies) {
        const float game = g_originalFov(evaluator, unused, applies);
        const auto* context = Read<const uint8_t*>(evaluator, kEvaluatorContext);
        if (!ForPlayer(context)) {
            return game;
        }
        if (t_close) {
            return 1.0f;
        }
        const float* target = Vec3(context, kContextTargetPosition);
        const float* eye = Vec3(context, kContextEye);
        const float* view = Vec3(context, kContextView);
        float along = 0.0f;
        float targetLength = 0.0f;
        float viewLength = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            const float toTarget = target[axis] - eye[axis];
            along += toTarget * view[axis];
            targetLength += toTarget * toTarget;
            viewLength += view[axis] * view[axis];
        }
        if (targetLength <= 0.0f || viewLength <= 0.0f) {
            return game;
        }
        const float cosine = std::clamp(along / std::sqrt(targetLength * viewLength), -1.0f, 1.0f);
        const float angle = std::acos(cosine) * 57.29578f;
        const auto* cones = Read<const uint8_t*>(context, kContextCones);
        // Heard, the cones reach all round, and behind his eyes he counts what he hears.
        const float peripheralHalf =
            t_heard ? kPeripheralSpan / 2.0f : Read<float>(cones, kPeripheralAngle) / 2.0f;
        if (angle > peripheralHalf) {
            return t_heard ? kHeardBehind : 0.0f;
        }
        if (angle < t_focusHalf || peripheralHalf <= t_focusHalf) {
            return 1.0f;
        }
        const float across = (angle - t_focusHalf) / (peripheralHalf - t_focusHalf);
        return (kPeripheralNearFocus + (kPeripheralAtEdge - kPeripheralNearFocus) * across) * t_motion;
    }

    float __fastcall SamplingDetour(void* evaluator, void* unused, bool* applies) {
        const float game = g_originalSampling(evaluator, unused, applies);
        const auto* context = Read<const uint8_t*>(evaluator, kEvaluatorContext);
        if (!ForPlayer(context) || g_targetDistance(context) > kCloseRange) {
            return game;
        }
        return std::min(game * kCloseSampling, 1.0f);
    }
}

void InstallEyesight() {
    if (!g_conesSet || !g_targetDistance ||
        !FCSE::ApiPointer()->MidHook(g_conesSet.get(), &OnConesSet) ||
        !Hook(g_distance, &DistanceDetour, &g_originalDistance) ||
        !Hook(g_fov, &FovDetour, &g_originalFov) ||
        !Hook(g_sampling, &SamplingDetour, &g_originalSampling)) {
        FCSE::Logf("eyesight: the soldiers' vision was not found in this build");
    }
}
