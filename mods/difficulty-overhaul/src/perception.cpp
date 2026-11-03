// Perception: from how far a soldier recognises the player and notices something, by how the player
// stands and moves, where he is in the soldier's view, grass, the camo suit, cover, night and the
// soldier's state. The game's own checks of distance, view, stance and movement give way to it.
#include "engine/combat_log.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <mutex>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with two stack arguments, and with one, which a free function spells __fastcall.
    using EvaluateFn = float(__fastcall*)(void* evaluator, void* unused, void* context, bool* applies);
    using CheckFn = float(__fastcall*)(void* check, void* unused, bool* applies);
    using TargetDistanceFn = float(__fastcall*)(const void* context);
    using TimeOfDayFn = float(__fastcall*)(void* senses);
    using GetCurrentVehicleFn = void*(__cdecl*)(void* pawn);
    using HierarchyFn = const uint8_t*(__fastcall*)(const void* object);

    // CVisibilityEvaluator::Evaluate, the product of a soldier's checks of how visible a target is.
    FCSE::Relocation<EvaluateFn> g_evaluate{FCSE::Uplay(0x00A1D8A0)};
    EvaluateFn g_originalEvaluate = nullptr;
    // CSensorySystem::AdjustFOV past its range caps, once one soldier's cones for one target are set.
    FCSE::Relocation<uint8_t*> g_conesSet{FCSE::Pattern(
        "8B 45 10 8B 80 ?? ?? ?? ?? 50 E8 ?? ?? ?? ?? 83 C4 04 84 C0 74 ?? 8B 4E 04 F3 0F 10 05 ?? ?? ?? ?? "
        "F3 0F 11 41 0C")};
    // The ComputeVisibility of the checks the model stands in for: CDistanceEvaluator,
    // CFOVEvaluator, CStanceEvaluator and CSpeedEvaluator. Then CGrassEvaluator's, asked whether grass
    // or the camo suit hides the player, and CPawnSamplingEvaluator's, a third for each of three
    // points on his body a ray reaches.
    FCSE::Relocation<uint8_t*> g_distance{
        FCSE::Pattern("8B 44 24 04 83 EC 1C 56 8B F1 C6 00 01 8B 46 24 80 78 3E 00 57 0F 85")};
    FCSE::Relocation<uint8_t*> g_fov{FCSE::Uplay(0x00A21380)};
    FCSE::Relocation<uint8_t*> g_stance{FCSE::Pattern(
        "8B 44 24 04 53 8B D9 C6 00 01 8B 4B 24 56 57 8B 79 0C 85 FF 74 ?? 83 3D ?? ?? ?? ?? 00 75 ?? "
        "33 C9 E8 ?? ?? ?? ?? 8B 17")};
    FCSE::Relocation<uint8_t*> g_speed{FCSE::Pattern(
        "8B 44 24 04 83 EC 14 56 57 8B F9 C6 00 01 8B 4F 24 8B 71 0C 85 F6 74 ?? 83 3D ?? ?? ?? ?? 00 "
        "75 ?? 33 C9 E8")};
    FCSE::Relocation<uint8_t*> g_grass{FCSE::Uplay(0x00A226C0)};
    FCSE::Relocation<uint8_t*> g_sampling{FCSE::Uplay(0x00A21D90)};
    // CVisibilityContext::GetTargetDistance, CSensorySystem::GetTimeOfDayFOVMultiplier, and the
    // vehicle a pawn sits in.
    FCSE::Relocation<TargetDistanceFn> g_targetDistance{FCSE::Uplay(0x00614F50)};
    FCSE::Relocation<TimeOfDayFn> g_timeOfDay{FCSE::Uplay(0x00A01200)};
    FCSE::Relocation<GetCurrentVehicleFn> g_currentVehicle{FCSE::Uplay(0x000E7330)};
    // CGrassEvaluator::ComputeVisibility's concealment: its ramp with distance, whose full and start
    // distances are operands kFullOperand and kFromOperand bytes in, then its factor by the soldier's
    // state, whose in-combat factor is kCombatOperand bytes in. Then its camouflage, whose distances
    // past which the suit hides the player from an alerted and an idle soldier are operands
    // kAlertedOperand and kIdleOperand bytes in.
    FCSE::Relocation<uint8_t*> g_concealment{
        FCSE::Pattern("D9 05 ?? ?? ?? ?? 83 EC 08 D9 5C 24 04 8B CE D9 05 ?? ?? ?? ?? D9 1C 24 E8 ?? ?? "
                      "?? ?? 51 8B CF D9 1C 24 E8 ?? ?? ?? ?? D9 5C 24 0C 8B 47 24 80 78 3E 00 75 ?? 80 "
                      "78 3F 00 75 ?? 80 78 3D 00 F3 0F 10 4C 24 ?? F3 0F 10 05 ?? ?? ?? ?? 74 ?? F3 0F "
                      "59 0D ?? ?? ?? ?? EB ?? F3 0F 59 0D ?? ?? ?? ?? EB ?? F3 0F 10 4C 24 ?? F3 0F 59 "
                      "0D")};
    constexpr ptrdiff_t kFullOperand = 2;
    constexpr ptrdiff_t kFromOperand = 17;
    constexpr ptrdiff_t kCombatOperand = 109;
    FCSE::Relocation<uint8_t*> g_camouflage{
        FCSE::Pattern("80 7B 2B 00 74 ?? 8B 4F 24 80 79 3E 00 75 ?? 80 79 3F 00 75 ?? 80 79 3D 00 74 ?? "
                      "E8 ?? ?? ?? ?? D9 05 ?? ?? ?? ?? D9 C9 DF F1 DD D8 77 ?? 8B 4F 24 80 79 3D 00 75 "
                      "?? E8 ?? ?? ?? ?? D9 05")};
    constexpr ptrdiff_t kAlertedOperand = 34;
    constexpr ptrdiff_t kIdleOperand = 62;

    // AdjustFOV's frame at the hook: whether the soldier aims through a sniper scope, whether the
    // target is a player, and the target agent. Its sensory system's owner, the share of the cones
    // night takes at most, and how much farther a scope reaches.
    constexpr ptrdiff_t kFrameScope = 0x0D;
    constexpr ptrdiff_t kFrameTargetIsPlayer = 0x0E;
    constexpr ptrdiff_t kFrameTarget = 0x24;
    constexpr ptrdiff_t kSensesOwner = 0x10;
    constexpr ptrdiff_t kSensesNightLoss = 0x40;
    constexpr ptrdiff_t kSensesScopeReach = 0x44;
    // CVisibilityEvaluator's checks, how many, and whether its last evaluation applied. A check's
    // context and weight, and the vtable slot of its ComputeVisibility.
    constexpr ptrdiff_t kChecks = 0x08;
    constexpr ptrdiff_t kCheckCount = 0x0C;
    constexpr ptrdiff_t kEvaluatorApplies = 0x15;
    constexpr ptrdiff_t kCheckContext = 0x24;
    constexpr ptrdiff_t kCheckWeight = 0x2C;
    constexpr ptrdiff_t kComputeSlot = 0x14;
    // A visibility context's cones, target, eye, target position, view direction, and whether the
    // soldier is alerted, fighting, or just out of a fight; each cone a length in metres then a full
    // angle in degrees.
    constexpr ptrdiff_t kContextCones = 0x04;
    constexpr ptrdiff_t kContextTarget = 0x0C;
    constexpr ptrdiff_t kContextEye = 0x10;
    constexpr ptrdiff_t kContextTargetPosition = 0x1C;
    constexpr ptrdiff_t kContextView = 0x28;
    constexpr ptrdiff_t kContextAlerted = 0x3D;
    constexpr ptrdiff_t kContextFighting = 0x3E;
    constexpr ptrdiff_t kContextAfterFight = 0x3F;
    constexpr ptrdiff_t kFocusLength = 0x08;
    constexpr ptrdiff_t kFocusAngle = 0x0C;
    constexpr ptrdiff_t kPeripheralLength = 0x1C;
    constexpr ptrdiff_t kPeripheralAngle = 0x20;
    // An object's class hierarchy, from the vtable slot that returns it: its depth, and the class at
    // depth six, CPawnAgent's, which animals do not share.
    constexpr ptrdiff_t kHierarchySlot = 0x04;
    constexpr ptrdiff_t kHierarchyDepth = 0x04;
    constexpr ptrdiff_t kHierarchySixth = 0x1C;

    // Hardcore: from how far a soldier recognises the player by day, in the open, straight ahead of
    // him and idle: walking upright, moving crouched, and still, standing or crouched.
    constexpr float kRecogniseUpright = 60.0f;
    constexpr float kRecogniseCrouched = 50.0f;
    constexpr float kRecogniseStill = 40.0f;
    // Hardcore: how much farther than that he notices something. In daylight and in the open, a
    // player moving straight ahead of him catches his eye from farther, upright and crouched.
    constexpr float kNoticeShare = 1.2f;
    constexpr float kUprightCatchesEye = 100.0f;
    constexpr float kCrouchedCatchesEye = 70.0f;
    // Hardcore: at his side, in high grass crouched, and alerted or fighting.
    constexpr float kAtSide = 0.5f;
    constexpr float kInGrass = 0.5f;
    constexpr float kAlert = 1.25f;
    // Hardcore: the most a soldier recognises and notices from at night.
    constexpr float kNightRecognise = 25.0f;
    constexpr float kNightNotice = 30.0f;
    // Hardcore: inside this, the camo suit no longer hides a still player in high grass.
    constexpr float kCamouflaged = 3.0f;
    // Hardcore: a player moving upright at least this fast is noticed behind a soldier within the
    // first distance and sensed in full within the second; crouched, he never is.
    constexpr float kMovingSpeed = 0.5f;
    constexpr float kHeardRange = 20.0f;
    constexpr float kArmsLength = 2.0f;
    // One ray of the three that reaches the player, his head over a wall, shows him as plainly as
    // all three.
    constexpr float kSamplingRays = 3.0f;
    // The cones' reach and span against the player: the game's cap on a cone against him, half a
    // turn, a full one. Then the grass check's concealment made a test of whether grass hides him at
    // all, its in-combat factor as from an idle soldier, and the camo suit's reach.
    constexpr float kMaxReach = 150.0f;
    constexpr float kHalfTurn = 180.0f;
    constexpr float kFullTurn = 360.0f;
    float g_grassFrom = 0.0f;
    float g_grassFull = 0.01f;
    float g_combatConcealment = 0.9f;
    float g_camouflageFrom = kCamouflaged;

    // What AdjustFOV found on this thread for the check of the same soldier and target that follows
    // it: the player as the target, unless he sits in a vehicle, and the soldier; half the focus
    // cone's angle before it is widened; how much farther a scope reaches; 0 by day to 1 at night;
    // and whether the player moves upright close enough behind him to be heard, or within arm's
    // length.
    struct Look {
        void* player = nullptr;
        void* soldier = nullptr;
        float focusHalf = 0.0f;
        float scope = 1.0f;
        float night = 0.0f;
        bool heard = false;
        bool close = false;
    };
    thread_local Look t_look;

    enum Zone { kFocus, kSide, kBehind };
    const char* const kZoneNames[] = {"in his focus", "at his side", "behind him"};

    // What the soldier's own checks of cover, grass and the camo suit found.
    struct Hiding {
        float cover = 1.0f;
        bool grass = false;
        bool camouflaged = false;
    };

    // The most each soldier, by entity id, has made of the player lately, 1 noticing and 2
    // recognising, logged as it rises; once below it for a while, it is forgotten.
    struct Seen {
        int level = 0;
        double below = -1.0;
        double traced = -1.0;
    };
    std::unordered_map<uint32_t, Seen> g_seen;
    std::mutex g_seenLock;
    constexpr double kForgetSeconds = 3.0;
    // Every check of the player as a line, at most this often per soldier, for tracing the model.
    constexpr bool kTrace = false;
    constexpr double kTraceSeconds = 0.5;

    // The class at depth six of an agent's hierarchy, 0 when it is shallower.
    uint32_t SixthClass(const void* agent) {
        const uint8_t* info = Read<HierarchyFn>(Read<const void*>(agent, 0), kHierarchySlot)(agent);
        return Read<uint32_t>(info, kHierarchyDepth) > 5 ? Read<uint32_t>(info, kHierarchySixth) : 0;
    }

    void OnConesSet(FCSE_MidHookContext* ctx) {
        const auto* frame = reinterpret_cast<const uint8_t*>(ctx->esp);
        if (Read<uint8_t>(frame, kFrameTargetIsPlayer) == 0) {
            return;
        }
        t_look = {};
        void* player = Read<void*>(frame, kFrameTarget);
        auto* senses = reinterpret_cast<uint8_t*>(ctx->ebp);
        void* soldier = Read<void*>(senses, kSensesOwner);
        void* pawn = Shooting::Pawn(player);
        void* entity = pawn != nullptr ? Game::EntityOf(pawn) : nullptr;
        // Animals and the player in a vehicle keep the game's own sight.
        const uint32_t pawnAgent = soldier != nullptr ? SixthClass(soldier) : 0;
        if (entity == nullptr || pawnAgent == 0 || pawnAgent != SixthClass(player) ||
            g_currentVehicle(pawn) != nullptr) {
            return;
        }
        const auto* context = reinterpret_cast<const uint8_t*>(ctx->esi);
        auto* cones = Read<uint8_t*>(context, kContextCones);

        Look look;
        look.player = player;
        look.soldier = soldier;
        look.focusHalf = Read<float>(cones, kFocusAngle) / 2.0f;
        look.scope = Read<uint8_t>(frame, kFrameScope) != 0 ? Read<float>(senses, kSensesScopeReach) : 1.0f;
        const float loss = Read<float>(senses, kSensesNightLoss);
        look.night = loss > 0.0f ? std::clamp((1.0f - g_timeOfDay(senses)) / loss, 0.0f, 1.0f) : 0.0f;

        float velocity[3];
        Shooting::Velocity(pawn, velocity);
        const bool movingUpright = Shooting::Stance(pawn) == Shooting::kStanding &&
                                   std::hypot(velocity[0], velocity[1], velocity[2]) >= kMovingSpeed;
        const float* eye = Vec3(context, kContextEye);
        float at[3];
        Game::Position(entity, at);
        const float distance = std::hypot(at[0] - eye[0], at[1] - eye[1]);
        look.heard = movingUpright && distance <= kHeardRange;
        look.close = movingUpright && distance <= kArmsLength;
        t_look = look;

        const float reach = std::max(Read<float>(cones, kFocusLength), kMaxReach);
        const float span = look.heard ? kFullTurn : kHalfTurn;
        Write(cones, kFocusLength, reach);
        Write(cones, kPeripheralLength, std::max(Read<float>(cones, kPeripheralLength), reach));
        Write(cones, kFocusAngle, std::max(Read<float>(cones, kFocusAngle), span));
        Write(cones, kPeripheralAngle, std::max(Read<float>(cones, kPeripheralAngle), span));
    }

    // Runs the soldier's checks the model keeps, as the game weighs them. False when one does not
    // apply.
    bool Hide(void* evaluator, void* context, Hiding& hiding) {
        auto* const* checks = Read<void* const*>(evaluator, kChecks);
        const int count = Read<int>(evaluator, kCheckCount);
        for (int i = 0; i < count; ++i) {
            void* check = checks[i];
            const uintptr_t compute = Read<uintptr_t>(Read<const void*>(check, 0), kComputeSlot);
            const float weight = Read<float>(check, kCheckWeight);
            if (compute == g_distance.address() || compute == g_fov.address() ||
                compute == g_stance.address() || compute == g_speed.address() || weight == 0.0f) {
                continue;
            }
            Write(check, kCheckContext, context);
            bool applies = true;
            float value = reinterpret_cast<CheckFn>(compute)(check, nullptr, &applies);
            if (!applies) {
                return false;
            }
            if (compute == g_grass.address()) {
                hiding.camouflaged = value == 0.0f;
                hiding.grass = value < 1.0f;
                continue;
            }
            if (compute == g_sampling.address()) {
                value = std::min(value * kSamplingRays, 1.0f);
            }
            hiding.cover *= value == 0.0f ? 0.0f : 1.0f - (1.0f - value) * weight;
        }
        return true;
    }

    Zone ZoneOf(const uint8_t* context, float focusHalf) {
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
            return kFocus;
        }
        const float angle =
            std::acos(std::clamp(along / std::sqrt(targetLength * viewLength), -1.0f, 1.0f)) * 57.29578f;
        return angle <= focusHalf ? kFocus : angle <= kHalfTurn / 2.0f ? kSide : kBehind;
    }

    // `distance` moved toward `limit` by `night`, when it is farther.
    float AtNight(float distance, float limit, float night) {
        return distance + (std::min(distance, limit) - distance) * night;
    }

    // The visibility that crosses the soldier's fuzzy line at `notice` and his clear line at
    // `recognise`.
    float Visibility(float distance, float recognise, float notice, void* soldier) {
        const float fuzzy = Shooting::FuzzyLine(soldier);
        const float clear = Shooting::ClearLine(soldier);
        if (distance <= recognise) {
            return clear + (1.0f - clear) * (1.0f - distance / recognise);
        }
        if (distance <= notice) {
            return fuzzy + (clear - fuzzy) * (notice - distance) / (notice - recognise);
        }
        return 0.0f;
    }

    void Report(const Look& look, float visibility, float distance, const char* moving, Zone zone,
                const Hiding& hiding, const char* state, float notice, float recognise) {
        const int level = visibility >= Shooting::ClearLine(look.soldier)   ? 2
                          : visibility >= Shooting::FuzzyLine(look.soldier) ? 1
                                                                            : 0;
        const double now = Game::Now();
        const uint32_t id = Game::Id(Game::EntityOf(Shooting::Pawn(look.soldier)));
        std::lock_guard<std::mutex> lock(g_seenLock);
        if (g_seen.size() > 512) {
            g_seen.clear();
        }
        Seen& seen = g_seen[id];
        if (kTrace && now - seen.traced >= kTraceSeconds) {
            seen.traced = now;
            CombatLogf("soldier %08X perception trace: %.1f m, %s, %s, %s; visibility %.2f, lines %.2f "
                       "and %.2f; notices from %.0f m, recognises from %.0f m",
                       id, distance, moving, kZoneNames[zone], state, visibility,
                       Shooting::FuzzyLine(look.soldier), Shooting::ClearLine(look.soldier), notice,
                       recognise);
        }
        if (level >= seen.level) {
            seen.below = -1.0;
        } else if (seen.below < 0.0) {
            seen.below = now;
        } else if (now - seen.below > kForgetSeconds) {
            seen = {level, -1.0};
        }
        if (level <= seen.level) {
            return;
        }
        seen.level = level;
        CombatLogf("soldier %08X perception: %s the player at %.1f m, %s, %s%s, %s; cover %.2f, "
                   "night %.2f; notices from %.0f m, recognises from %.0f m",
                   id, level == 2 ? "recognises" : "notices", distance, moving, kZoneNames[zone],
                   hiding.camouflaged ? ", hidden by the camo suit" : hiding.grass ? ", in grass" : "",
                   state, hiding.cover, look.night, notice, recognise);
    }

    float __fastcall EvaluateDetour(void* evaluator, void* unused, void* context, bool* applies) {
        const auto* at = static_cast<const uint8_t*>(context);
        const Look look = t_look;
        if (look.player == nullptr || Read<const void*>(at, kContextTarget) != look.player) {
            return g_originalEvaluate(evaluator, unused, context, applies);
        }
        *applies = true;
        Write<bool>(evaluator, kEvaluatorApplies, true);
        Hiding hiding;
        if (!Hide(evaluator, context, hiding)) {
            *applies = false;
            Write<bool>(evaluator, kEvaluatorApplies, false);
            return hiding.cover;
        }

        void* pawn = Shooting::Pawn(look.player);
        float velocity[3];
        Shooting::Velocity(pawn, velocity);
        const bool upright = Shooting::Stance(pawn) == Shooting::kStanding;
        const bool moving = std::hypot(velocity[0], velocity[1], velocity[2]) >= kMovingSpeed;
        const float distance = g_targetDistance(context);
        const Zone zone = ZoneOf(at, look.focusHalf);
        const bool alert = Read<bool>(at, kContextAlerted) || Read<bool>(at, kContextFighting) ||
                           Read<bool>(at, kContextAfterFight);

        float recognise = moving ? (upright ? kRecogniseUpright : kRecogniseCrouched) : kRecogniseStill;
        recognise *= (zone == kSide ? kAtSide : 1.0f) * (hiding.grass ? kInGrass : 1.0f) * hiding.cover;
        float notice = recognise * kNoticeShare;
        if (zone == kFocus && moving && look.night < 0.5f && !hiding.grass && hiding.cover >= 0.99f) {
            notice = std::max(notice, upright ? kUprightCatchesEye : kCrouchedCatchesEye);
        }
        recognise = AtNight(recognise, kNightRecognise, look.night);
        notice = AtNight(notice, kNightNotice, look.night);
        const float sharpness = (alert ? kAlert : 1.0f) * (zone == kFocus ? look.scope : 1.0f);
        recognise *= sharpness;
        notice *= sharpness;
        if (hiding.camouflaged) {
            recognise = notice = kCamouflaged;
        }

        float visibility = Visibility(distance, recognise, notice, look.soldier);
        if (zone == kBehind) {
            visibility = look.close ? 1.0f : look.heard ? Shooting::FuzzyLine(look.soldier) : 0.0f;
        } else if (distance <= kArmsLength) {
            visibility = 1.0f;
        }
        Report(look, visibility, distance,
               moving ? (upright ? "walking upright" : "moving crouched") : "still", zone, hiding,
               alert ? "alert" : "idle", notice, recognise);
        return visibility;
    }
}

void InstallPerception() {
    if (!g_conesSet || !g_targetDistance || !g_timeOfDay || !g_currentVehicle || !g_distance ||
        !g_fov || !g_stance || !g_speed || !g_grass || !g_sampling ||
        !Hook(g_evaluate, &EvaluateDetour, &g_originalEvaluate) ||
        !FCSE::ApiPointer()->MidHook(g_conesSet.get(), &OnConesSet)) {
        FCSE::Logf("perception: the soldiers' vision was not found in this build");
        return;
    }
    if (!RedirectOperand(g_concealment, kFromOperand, &g_grassFrom) ||
        !RedirectOperand(g_concealment, kFullOperand, &g_grassFull) ||
        !RedirectOperand(g_concealment, kCombatOperand, &g_combatConcealment) ||
        !RedirectOperand(g_camouflage, kAlertedOperand, &g_camouflageFrom) ||
        !RedirectOperand(g_camouflage, kIdleOperand, &g_camouflageFrom)) {
        FCSE::Logf("perception: grass and the camo suit were not found in this build");
    }
}
