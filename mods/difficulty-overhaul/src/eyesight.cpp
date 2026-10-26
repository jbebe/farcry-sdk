// Eyesight: how soldiers see the player. The focus cone reaches farther and keeps the player fully
// visible out to half its length; he sees across 180 degrees, but outside his focus the player is
// only ever noticed: enough for a soldier to turn and look, never enough to see him as an enemy. Up
// close, the part of the player that shows over cover counts double.
#include "engine/memory.h"
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
    // CDistanceEvaluator::ComputeVisibility, which the address library does not know, and
    // CFOVEvaluator::ComputeVisibility; CPawnSamplingEvaluator::ComputeVisibility, a third for each
    // of three points on the target's body a ray reaches; CVisibilityContext::GetTargetDistance.
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

    // Hardcore: the focus cone's reach against the game's (100 m by day for a rifleman, where the
    // game's is 60), the angle both cones span, and the share of the focus cone the player is fully
    // visible within, where the game's is a quarter. A soldier sees the player plainly only inside
    // both cones, and his threat search looks at nothing else, so both span the whole 180 degrees
    // and reach as far; the FOV evaluator keeps the game's focus angle for where he counts fully.
    constexpr float kFocusReach = 100.0f / 60.0f;
    constexpr float kPeripheralSpan = 180.0f;
    constexpr float kFullVisibility = 0.5f;
    // Hardcore: how visible the player is in the periphery, at the focus cone's edge and at the
    // peripheral cone's: above every state's "something is there", below idle's and alert's "I see
    // him".
    constexpr float kPeripheralNearFocus = 0.55f;
    constexpr float kPeripheralAtEdge = 0.35f;
    // Hardcore: within this range a head over cover is as plain as a whole man's top half, so the
    // share of the player's body the rays reach counts this many times over, up to all of it.
    constexpr float kCloseRange = 40.0f;
    constexpr float kCloseSampling = 2.0f;
    // The game's cap on a cone against the player, unless he looks through a scope.
    constexpr float kMaxFocusLength = 150.0f;

    // The player's agent, the target AdjustFOV last flagged as a player.
    std::atomic<const void*> g_player{nullptr};
    // Half the focus cone's angle AdjustFOV gave the player on this thread, before both cones were
    // widened: within it he counts fully.
    thread_local float t_focusHalf = 0.0f;

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

    void OnConesSet(FCSE_MidHookContext* ctx) {
        const auto* frame = reinterpret_cast<const uint8_t*>(ctx->esp);
        if (Read<uint8_t>(frame, kTargetIsPlayer) == 0) {
            return;
        }
        g_player.store(Read<const void*>(frame, kTarget), std::memory_order_relaxed);
        auto* cones = Read<uint8_t*>(reinterpret_cast<const void*>(ctx->esi), kContextCones);
        float focus = Read<float>(cones, kFocusLength);
        if (focus < kMaxFocusLength) {
            focus = std::min(focus * kFocusReach, kMaxFocusLength);
            Write(cones, kFocusLength, focus);
        }
        Write(cones, kPeripheralLength, std::max(Read<float>(cones, kPeripheralLength), focus));
        const float span = std::max(Read<float>(cones, kPeripheralAngle), kPeripheralSpan);
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
        float toTarget[3];
        float view[3];
        float along = 0.0f;
        float targetLength = 0.0f;
        float viewLength = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            const ptrdiff_t offset = axis * sizeof(float);
            toTarget[axis] = Read<float>(context, kContextTargetPosition + offset) -
                             Read<float>(context, kContextEye + offset);
            view[axis] = Read<float>(context, kContextView + offset);
            along += toTarget[axis] * view[axis];
            targetLength += toTarget[axis] * toTarget[axis];
            viewLength += view[axis] * view[axis];
        }
        if (targetLength <= 0.0f || viewLength <= 0.0f) {
            return game;
        }
        const float cosine = std::clamp(along / std::sqrt(targetLength * viewLength), -1.0f, 1.0f);
        const float angle = std::acos(cosine) * 57.29578f;
        const auto* cones = Read<const uint8_t*>(context, kContextCones);
        const float focusHalf = t_focusHalf;
        const float peripheralHalf = Read<float>(cones, kPeripheralAngle) / 2.0f;
        if (angle > peripheralHalf) {
            return 0.0f;
        }
        if (angle < focusHalf || peripheralHalf <= focusHalf) {
            return 1.0f;
        }
        const float across = (angle - focusHalf) / (peripheralHalf - focusHalf);
        return kPeripheralNearFocus + (kPeripheralAtEdge - kPeripheralNearFocus) * across;
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
