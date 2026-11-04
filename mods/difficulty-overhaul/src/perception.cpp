// Perception: from how far a soldier recognises the player and notices something, by how the player
// stands and moves, where he is in the soldier's view, grass, bushes, the camo suit, cover, night and
// the soldier's state. The game's own checks of distance, view, stance and movement give way to it.
#include "engine/combat_log.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <windows.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
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
    // __thiscall with four stack arguments; `invert` reads the bonus negated.
    using BonusFn = float(__fastcall*)(void* bonuses, void* unused, const uint32_t* category,
                                       const uint32_t* name, float base, int invert);
    using InHighGrassFn = bool(__fastcall*)(void* unused, void* unused2, const float* point);
    using GroundHeightFn = float(__cdecl*)(float x, float y);
    using FogSetterFn = void(__fastcall*)(void* engine, void* unused, float value);
    // __thiscall with two stack arguments and with eight.
    using TreeVisibilityFn = float(__fastcall*)(void* trees, void* unused, const float* from, const float* line);
    using TreeHidesFn = float(__fastcall*)(void* trees, void* unused, const void* tree, float x, float y, float z,
                                           const float* from, const float* to, const float* direction,
                                           float lengthSquared);
    using CamouflageFn = float(__fastcall*)(const void* simulation);

    // CVisibilityEvaluator::Evaluate, the product of a soldier's checks of how visible a target is.
    FCSE::Relocation<EvaluateFn> g_evaluate{FCSE::Uplay(0x00A1D8A0)};
    EvaluateFn g_originalEvaluate = nullptr;
    // CSensorySystem::AdjustFOV past its range caps, once one soldier's cones for one target are set.
    FCSE::Relocation<uint8_t*> g_conesSet{FCSE::Pattern(
        "8B 45 10 8B 80 ?? ?? ?? ?? 50 E8 ?? ?? ?? ?? 83 C4 04 84 C0 74 ?? 8B 4E 04 F3 0F 10 05 ?? ?? ?? ?? "
        "F3 0F 11 41 0C")};
    // The ComputeVisibility of the checks the model stands in for: CDistanceEvaluator,
    // CFOVEvaluator, CStanceEvaluator and CSpeedEvaluator. Then CGrassEvaluator's, asked whether grass
    // hides the player, and CPawnSamplingEvaluator's, a third for each of three points on his body a
    // ray reaches.
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
    // A pawn's bonus of one name from its bonus plans, the camo suit's `stealth` among them, and the
    // category the grass check asks it under.
    FCSE::Relocation<BonusFn> g_bonus{FCSE::Uplay(0x0007E280)};
    FCSE::Relocation<const uint32_t*> g_bonusCategory{FCSE::Uplay(0x00E2C55C)};
    // CStealthComponent::InHighGrass, which ignores its object, and the loaded terrain's height.
    FCSE::Relocation<InHighGrassFn> g_inHighGrass{FCSE::Uplay(0x006991D0)};
    GroundHeightFn g_groundHeight = nullptr;
    // The 3D engine's fog setters, which the environment manager hands this frame's fog: the
    // distance it is full at, and how much of the view it takes there.
    FCSE::Relocation<FogSetterFn> g_setFogEnd{FCSE::Uplay(0x0032F640)};
    FCSE::Relocation<FogSetterFn> g_setFogAmount{FCSE::Uplay(0x0032F660)};
    FogSetterFn g_originalSetFogEnd = nullptr;
    FogSetterFn g_originalSetFogAmount = nullptr;
    float g_fogEnd = 0.0f;
    float g_fogAmount = 0.0f;
    // CAABBPartitionManager::GetCombinedTreeVisibility and CamouflageHitTestEntry, how much the
    // RealTrees along a line, and one of them, hide of it; a RealTree's camouflage less its shot-off leaves.
    FCSE::Relocation<TreeVisibilityFn> g_treeVisibility{FCSE::Uplay(0x005318F0)};
    FCSE::Relocation<TreeHidesFn> g_treeHides{FCSE::Uplay(0x0052FFE0)};
    FCSE::Relocation<CamouflageFn> g_camouflage{FCSE::Uplay(0x00467F90)};
    TreeVisibilityFn g_originalTreeVisibility = nullptr;
    TreeHidesFn g_originalTreeHides = nullptr;
    // CGrassEvaluator::ComputeVisibility's concealment: its ramp with distance, whose full and start
    // distances are operands kFullOperand and kFromOperand bytes in, then its factor by the soldier's
    // state, whose in-combat factor is kCombatOperand bytes in.
    FCSE::Relocation<uint8_t*> g_concealment{
        FCSE::Pattern("D9 05 ?? ?? ?? ?? 83 EC 08 D9 5C 24 04 8B CE D9 05 ?? ?? ?? ?? D9 1C 24 E8 ?? ?? "
                      "?? ?? 51 8B CF D9 1C 24 E8 ?? ?? ?? ?? D9 5C 24 0C 8B 47 24 80 78 3E 00 75 ?? 80 "
                      "78 3F 00 75 ?? 80 78 3D 00 F3 0F 10 4C 24 ?? F3 0F 10 05 ?? ?? ?? ?? 74 ?? F3 0F "
                      "59 0D ?? ?? ?? ?? EB ?? F3 0F 59 0D ?? ?? ?? ?? EB ?? F3 0F 10 4C 24 ?? F3 0F 59 "
                      "0D")};
    constexpr ptrdiff_t kFullOperand = 2;
    constexpr ptrdiff_t kFromOperand = 17;
    constexpr ptrdiff_t kCombatOperand = 109;
    // The hash of the camo suit's bonus.
    constexpr uint32_t kStealthBonus = 0x4BE05598;

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
    // A CVehicle's properties, which hold its seats: where they are and how many, each kSeatSize. A
    // seat's role, the low half of its occupant's entity id, its state; the driver's role, and seated.
    constexpr ptrdiff_t kVehicleProperties = 0x10;
    constexpr ptrdiff_t kSeats = 0x0C;
    constexpr ptrdiff_t kSeatCount = 0x10;
    constexpr ptrdiff_t kSeatSize = 0x50;
    constexpr ptrdiff_t kSeatRole = 0x10;
    constexpr ptrdiff_t kSeatOccupant = 0x28;
    constexpr ptrdiff_t kSeatState = 0x30;
    constexpr int32_t kDriverSeat = 1;
    constexpr int32_t kSeated = 4;
    // A RealTree the partition manager holds: its simulation, whose skeleton is its species'. In the
    // skeleton, from the tree's foot, its box's least and greatest corners, and the centre and radius
    // of the sphere its camouflage counts in.
    constexpr ptrdiff_t kTreeSimulation = 0x08;
    constexpr ptrdiff_t kSimulationSkeleton = 0x4A8;
    constexpr ptrdiff_t kSkeletonLeast = 0x60;
    constexpr ptrdiff_t kSkeletonGreatest = 0x6C;
    constexpr ptrdiff_t kSkeletonCamouflageCentre = 0xC0;
    constexpr ptrdiff_t kSkeletonCamouflageRadius = 0xCC;

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
    // Hardcore: a driver sees only through his windscreen, this wide.
    constexpr float kWindscreen = 50.0f;
    // Hardcore: the most a soldier recognises and notices from at night.
    constexpr float kNightRecognise = 25.0f;
    constexpr float kNightNotice = 30.0f;
    // Hardcore: the most a soldier recognises and notices from in the heaviest fog, a sandstorm's,
    // full at 30 m with 0.98 of the view; lighter fog limits him in proportion to its reach.
    constexpr float kFogRecognise = 40.0f;
    constexpr float kFogNotice = 45.0f;
    constexpr float kHeaviestFogReach = 30.0f / 0.98f;
    // Hardcore: from how far a soldier not fighting him notices and recognises a player in the camo
    // suit, crouched and still in high grass.
    constexpr float kCamouflaged = 3.0f;
    // Hardcore: a player moving upright at least this fast is noticed behind a soldier within the
    // first distance and sensed in full within the second; crouched, he never is.
    constexpr float kMovingSpeed = 0.5f;
    constexpr float kHeardRange = 20.0f;
    constexpr float kArmsLength = 2.0f;
    // One ray of the three that reaches the player, his head over a wall, shows him as plainly as
    // all three.
    constexpr float kSamplingRays = 3.0f;
    // The walk along a soldier's line: the top of the tall grass, how far below the lower head's
    // height the line must run, how near either of them grass is his own, the step, and the hold.
    constexpr float kGrassTop = 1.2f;
    constexpr float kBelowHeads = 0.3f;
    constexpr float kOwnGrass = 2.0f;
    constexpr float kWalkStep = 1.0f;
    constexpr double kWalkHold = 0.25;
    // A RealTree is a bush when its camouflage reaches lower than the first height above its foot and
    // it stands lower than the second. Within the share of its foliage a bush hides in full, fading out
    // to its edge.
    constexpr float kBushCamouflageBelow = 1.0f;
    constexpr float kBushTopBelow = 6.0f;
    constexpr float kBushSolid = 0.75f;
    // The cones' reach and span against the player: the game's cap on a cone against him, half a
    // turn, a full one. Then the grass check's concealment made a test of whether grass hides him at
    // all, and its in-combat factor as from an idle soldier.
    constexpr float kMaxReach = 150.0f;
    constexpr float kHalfTurn = 180.0f;
    constexpr float kFullTurn = 360.0f;
    float g_grassFrom = 0.0f;
    float g_grassFull = 0.01f;
    float g_combatConcealment = 0.9f;

    // What AdjustFOV found on this thread for the check of the same soldier and target that follows
    // it: the player as the target, unless he sits in a vehicle, and the soldier; half the focus
    // cone's angle before it is widened; how much farther a scope reaches; 0 by day to 1 at night;
    // whether the player moves upright close enough behind him to be heard, or within arm's length;
    // and, when the soldier drives, the way his vehicle faces.
    struct Look {
        void* player = nullptr;
        void* soldier = nullptr;
        float focusHalf = 0.0f;
        float scope = 1.0f;
        float night = 0.0f;
        bool heard = false;
        bool close = false;
        bool driving = false;
        float ahead[3] = {};
    };
    thread_local Look t_look;

    enum Zone { kFocus, kSide, kBehind };
    const char* const kZoneNames[] = {"in his focus", "at his side", "behind him"};

    // What the soldier's own checks of cover and grass found, whether a bush's foliage counted in that
    // cover, whether grass between them hides the player, and whether the camo suit does.
    struct Hiding {
        float cover = 1.0f;
        bool grass = false;
        bool behindBush = false;
        bool behindGrass = false;
        bool camouflaged = false;
    };

    // While the soldier's checks of the player run on this thread: the check's context, whose eye and
    // target position make the line the RealTrees are asked about, and whether a bush hid any of it.
    thread_local const uint8_t* t_treeLine = nullptr;
    thread_local bool t_behindBush = false;

    // The most each soldier, by entity id, has made of the player lately, 1 noticing and 2
    // recognising, logged as it rises; once below it for a while, it is forgotten. Then when his grass
    // check last found the player in grass, and when his line was last walked and what it found.
    struct Seen {
        int level = 0;
        double below = -1.0;
        double traced = -1.0;
        double grass = -1.0;
        double walked = -1.0;
        bool behindGrass = false;
    };
    std::unordered_map<uint32_t, Seen> g_seen;
    std::mutex g_seenLock;
    constexpr double kForgetSeconds = 3.0;
    // Seconds a soldier's grass finding is held: his check sometimes misses the player for one look.
    constexpr double kGrassHold = 0.5;
    // Every check of the player as a line, at most this often per soldier, when the game is launched
    // with the switch; fc2drive's perception scenario reads it to see where the soldier looks.
    constexpr char kTraceSwitch[] = "-perceptiontrace";
    constexpr double kTraceSeconds = 0.25;
    bool g_trace = false;
    // While tracing, the walks since they were last logged, every kWalkLogSeconds: how many, and
    // their total and longest time in microseconds; and the performance counter's ticks a microsecond.
    constexpr double kWalkLogSeconds = 10.0;
    int g_walks = 0;
    double g_walkTotal = 0.0;
    double g_walkLongest = 0.0;
    double g_walksLogged = -1.0;
    double g_ticksPerMicro = 0.0;

    // The class at depth six of an agent's hierarchy, 0 when it is shallower.
    uint32_t SixthClass(const void* agent) {
        const uint8_t* info = Read<HierarchyFn>(Read<const void*>(agent, 0), kHierarchySlot)(agent);
        return Read<uint32_t>(info, kHierarchyDepth) > 5 ? Read<uint32_t>(info, kHierarchySixth) : 0;
    }

    // Whether the soldier `entity` sits in `vehicle`'s driving seat.
    bool Drives(const void* vehicle, const void* entity) {
        const uint8_t* properties = Read<const uint8_t*>(vehicle, kVehicleProperties);
        if (properties == nullptr || entity == nullptr) {
            return false;
        }
        const uint8_t* seats = Read<const uint8_t*>(properties, kSeats);
        const uint32_t count = Read<uint32_t>(properties, kSeatCount);
        const uint32_t id = Game::Id(entity);
        for (uint32_t i = 0; i < count; ++i) {
            const uint8_t* seat = seats + i * kSeatSize;
            if (Read<uint32_t>(seat, kSeatOccupant) == id && Read<int32_t>(seat, kSeatState) == kSeated) {
                return Read<int32_t>(seat, kSeatRole) == kDriverSeat;
            }
        }
        return false;
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
        void* soldierPawn = Shooting::Pawn(soldier);
        void* vehicle = g_currentVehicle(soldierPawn);
        look.driving = vehicle != nullptr && Drives(vehicle, Game::EntityOf(soldierPawn));
        if (look.driving) {
            Game::Forward(Game::EntityOf(vehicle), look.ahead);
        }
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

    // Whether the player's pawn has the camo suit's stealth from its bonus plans.
    bool WearsCamoSuit(void* pawn) {
        return pawn != nullptr && g_bonus && g_bonusCategory &&
               g_bonus(pawn, nullptr, g_bonusCategory.get(), &kStealthBonus, 0.0f, 0) > 0.0f;
    }

    // Where the player is in the soldier's view; a driver's is his windscreen, and beyond it is as
    // behind him.
    Zone ZoneOf(const uint8_t* context, const Look& look) {
        const float* target = Vec3(context, kContextTargetPosition);
        const float* eye = Vec3(context, kContextEye);
        const float* view = look.driving ? look.ahead : Vec3(context, kContextView);
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
        if (look.driving) {
            return angle <= kWindscreen / 2.0f ? kFocus : kBehind;
        }
        return angle <= look.focusHalf ? kFocus : angle <= kHalfTurn / 2.0f ? kSide : kBehind;
    }

    // `distance` moved toward `limit` by `night`, when it is farther.
    float AtNight(float distance, float limit, float night) {
        return distance + (std::min(distance, limit) - distance) * night;
    }

    void __fastcall SetFogEndDetour(void* engine, void* unused, float value) {
        g_fogEnd = value;
        g_originalSetFogEnd(engine, unused, value);
    }

    void __fastcall SetFogAmountDetour(void* engine, void* unused, float value) {
        g_fogAmount = value;
        g_originalSetFogAmount(engine, unused, value);
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

    const char* HidingName(const Hiding& hiding) {
        return hiding.camouflaged    ? ", hidden by the camo suit"
               : hiding.behindGrass ? ", behind grass"
               : hiding.grass       ? ", in grass"
                                    : "";
    }

    const char* BushName(const Hiding& hiding) { return hiding.behindBush ? ", behind a bush" : ""; }

    // Whether grass hides the player from soldier `id`: as his check found now, or within kGrassHold.
    bool HeldInGrass(uint32_t id, bool grass, double now) {
        std::lock_guard<std::mutex> lock(g_seenLock);
        Seen& seen = g_seen[id];
        if (grass) {
            seen.grass = now;
        }
        return now - seen.grass <= kGrassHold;
    }

    // Whether tall grass between them hides the player: grass where the soldier's line to the player's
    // head runs within the grass's height of the ground, and closer to it than either head is to its own.
    bool BehindGrass(const uint8_t* context) {
        const float* eye = Vec3(context, kContextEye);
        const float* head = Vec3(context, kContextTargetPosition);
        const float across = std::hypot(head[0] - eye[0], head[1] - eye[1]);
        const float lowerHead =
            std::min(eye[2] - g_groundHeight(eye[0], eye[1]), head[2] - g_groundHeight(head[0], head[1]));
        const float within = std::min(kGrassTop, lowerHead - kBelowHeads);
        for (float walked = kOwnGrass; walked <= across - kOwnGrass; walked += kWalkStep) {
            const float share = walked / across;
            const float point[3] = {eye[0] + (head[0] - eye[0]) * share, eye[1] + (head[1] - eye[1]) * share,
                                    eye[2] + (head[2] - eye[2]) * share};
            if (point[2] - g_groundHeight(point[0], point[1]) < within && g_inHighGrass(nullptr, nullptr, point)) {
                return true;
            }
        }
        return false;
    }

    // Counts a traced walk of `micros`, and logs the walks every kWalkLogSeconds. Under g_seenLock.
    void NoteWalk(double micros, double now) {
        if (g_walksLogged < 0.0) {
            g_walksLogged = now;
        }
        ++g_walks;
        g_walkTotal += micros;
        g_walkLongest = std::max(g_walkLongest, micros);
        if (now - g_walksLogged >= kWalkLogSeconds) {
            CombatLogf("perception: %d grass walks in %.0f s, %.1f us on average, %.1f us at the longest", g_walks,
                       now - g_walksLogged, g_walkTotal / g_walks, g_walkLongest);
            g_walksLogged = now;
            g_walks = 0;
            g_walkTotal = 0.0;
            g_walkLongest = 0.0;
        }
    }

    // BehindGrass for soldier `id`, walked again once kWalkHold has passed.
    bool HeldBehindGrass(uint32_t id, const uint8_t* context, double now) {
        {
            std::lock_guard<std::mutex> lock(g_seenLock);
            const Seen& seen = g_seen[id];
            if (now - seen.walked < kWalkHold) {
                return seen.behindGrass;
            }
        }
        LARGE_INTEGER start = {};
        LARGE_INTEGER end = {};
        if (g_trace) {
            QueryPerformanceCounter(&start);
        }
        const bool behind = BehindGrass(context);
        if (g_trace) {
            QueryPerformanceCounter(&end);
        }

        std::lock_guard<std::mutex> lock(g_seenLock);
        Seen& seen = g_seen[id];
        seen.walked = now;
        seen.behindGrass = behind;
        if (g_trace) {
            NoteWalk(static_cast<double>(end.QuadPart - start.QuadPart) / g_ticksPerMicro, now);
        }
        return behind;
    }

    // GetCombinedTreeVisibility of the line from the soldier's eye to the player's head, in place of
    // his vegetation check's to the player's torso, while his checks of the player run.
    float __fastcall TreeVisibilityDetour(void* trees, void* unused, const float* from, const float* line) {
        if (t_treeLine == nullptr) {
            return g_originalTreeVisibility(trees, unused, from, line);
        }
        const float* eye = Vec3(t_treeLine, kContextEye);
        const float* head = Vec3(t_treeLine, kContextTargetPosition);
        const float toHead[3] = {head[0] - eye[0], head[1] - eye[1], head[2] - eye[2]};
        return g_originalTreeVisibility(trees, unused, eye, toHead);
    }

    // How much the RealTree at (x, y, z) hides of the player's line. A bush counts by its foliage: an
    // upright column as wide as its narrower side and rounded at the top, met where the line passes
    // nearest to it across the ground. A tree keeps the game's sphere around its crown.
    float __fastcall TreeHidesDetour(void* trees, void* unused, const void* tree, float x, float y, float z,
                                     const float* from, const float* to, const float* direction,
                                     float lengthSquared) {
        if (t_treeLine == nullptr) {
            return g_originalTreeHides(trees, unused, tree, x, y, z, from, to, direction, lengthSquared);
        }
        const auto* simulation = Read<const uint8_t*>(tree, kTreeSimulation);
        const auto* skeleton = Read<const uint8_t*>(simulation, kSimulationSkeleton);
        const float* least = Vec3(skeleton, kSkeletonLeast);
        const float* greatest = Vec3(skeleton, kSkeletonGreatest);
        const float lowest =
            Vec3(skeleton, kSkeletonCamouflageCentre)[2] - Read<float>(skeleton, kSkeletonCamouflageRadius);
        const float foot = std::max(least[2], 0.0f);
        const float half = (greatest[2] - foot) / 2.0f;
        const float radius = std::min(greatest[0] - least[0], greatest[1] - least[1]) / 2.0f;
        if (lowest >= kBushCamouflageBelow || greatest[2] >= kBushTopBelow || half <= 0.0f || radius <= 0.0f) {
            return g_originalTreeHides(trees, unused, tree, x, y, z, from, to, direction, lengthSquared);
        }
        const float across[2] = {to[0] - from[0], to[1] - from[1]};
        const float acrossSquared = across[0] * across[0] + across[1] * across[1];
        const float along =
            acrossSquared > 0.0f
                ? std::clamp(((x - from[0]) * across[0] + (y - from[1]) * across[1]) / acrossSquared, 0.0f, 1.0f)
                : 0.0f;
        const float aside[2] = {from[0] + across[0] * along - x, from[1] + across[1] * along - y};
        const float above = std::max(from[2] + (to[2] - from[2]) * along - z - foot - half, 0.0f) / half;
        const float reach = (aside[0] * aside[0] + aside[1] * aside[1]) / (radius * radius) + above * above;
        if (reach >= 1.0f) {
            return 0.0f;
        }
        const float hidden =
            std::min((1.0f - std::sqrt(reach)) / (1.0f - kBushSolid), 1.0f) * g_camouflage(simulation);
        t_behindBush = t_behindBush || hidden > 0.0f;
        return hidden;
    }

    void Report(const Look& look, uint32_t id, double now, float visibility, float distance,
                const char* moving, Zone zone, const Hiding& hiding, const char* state, float notice,
                float recognise) {
        const int level = visibility >= Shooting::ClearLine(look.soldier)   ? 2
                          : visibility >= Shooting::FuzzyLine(look.soldier) ? 1
                                                                            : 0;
        std::lock_guard<std::mutex> lock(g_seenLock);
        if (g_seen.size() > 512) {
            g_seen.clear();
        }
        Seen& seen = g_seen[id];
        if (g_trace && now - seen.traced >= kTraceSeconds) {
            seen.traced = now;
            CombatLogf("soldier %08X perception trace: %.1f m, %s, %s%s%s%s, %s; cover %.2f; visibility %.2f, "
                       "lines %.2f and %.2f; fog full at %.0f m with %.2f; notices from %.0f m, "
                       "recognises from %.0f m",
                       id, distance, moving, kZoneNames[zone], HidingName(hiding), BushName(hiding),
                       look.driving ? ", driving" : "", state, hiding.cover,
                       visibility, Shooting::FuzzyLine(look.soldier), Shooting::ClearLine(look.soldier),
                       g_fogEnd, g_fogAmount, notice, recognise);
        }
        if (level >= seen.level) {
            seen.below = -1.0;
        } else if (seen.below < 0.0) {
            seen.below = now;
        } else if (now - seen.below > kForgetSeconds) {
            seen.level = level;
            seen.below = -1.0;
        }
        if (level <= seen.level) {
            return;
        }
        seen.level = level;
        CombatLogf("soldier %08X perception: %s the player at %.1f m, %s, %s%s%s, %s; cover %.2f, "
                   "night %.2f; notices from %.0f m, recognises from %.0f m",
                   id, level == 2 ? "recognises" : "notices", distance, moving, kZoneNames[zone],
                   HidingName(hiding), BushName(hiding), state, hiding.cover, look.night, notice, recognise);
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
        t_treeLine = at;
        t_behindBush = false;
        const bool applied = Hide(evaluator, context, hiding);
        t_treeLine = nullptr;
        hiding.behindBush = t_behindBush;
        if (!applied) {
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
        const Zone zone = ZoneOf(at, look);
        const bool fighting = Read<bool>(at, kContextFighting) || Read<bool>(at, kContextAfterFight);
        const bool alert = Read<bool>(at, kContextAlerted) || fighting;
        const double now = Game::Now();
        void* soldier = Game::EntityOf(Shooting::Pawn(look.soldier));
        const uint32_t id = Game::Id(soldier);
        hiding.grass = !upright && HeldInGrass(id, hiding.grass, now);
        hiding.camouflaged = hiding.grass && !moving && !fighting && WearsCamoSuit(pawn);

        float recognise = moving ? (upright ? kRecogniseUpright : kRecogniseCrouched) : kRecogniseStill;
        recognise *= (zone == kSide ? kAtSide : 1.0f) * (hiding.grass ? kInGrass : 1.0f) * hiding.cover;
        float notice = recognise * kNoticeShare;
        if (zone == kFocus && moving && look.night < 0.5f && !hiding.grass && hiding.cover >= 0.99f) {
            notice = std::max(notice, upright ? kUprightCatchesEye : kCrouchedCatchesEye);
        }
        // A scope reaches farther, but no farther than night or fog let anyone see.
        const float scope = zone == kFocus ? look.scope : 1.0f;
        recognise *= scope;
        notice *= scope;
        recognise = AtNight(recognise, kNightRecognise, look.night);
        notice = AtNight(notice, kNightNotice, look.night);
        // How far this frame's fog lets anyone see, relative to the heaviest fog's: its full distance
        // over how much of the view it takes.
        if (g_fogEnd > 0.0f && g_fogAmount > 0.0f) {
            const float fog = g_fogEnd / g_fogAmount / kHeaviestFogReach;
            recognise = std::min(recognise, kFogRecognise * fog);
            notice = std::min(notice, kFogNotice * fog);
        }
        if (alert) {
            recognise *= kAlert;
            notice *= kAlert;
        }
        if (hiding.camouflaged) {
            recognise = notice = kCamouflaged;
        }

        float visibility = Visibility(distance, recognise, notice, look.soldier);
        hiding.behindGrass = g_groundHeight != nullptr && visibility > 0.0f && zone != kBehind &&
                             distance > kArmsLength && HeldBehindGrass(id, at, now);
        if (zone == kBehind) {
            visibility = look.close ? 1.0f : look.heard ? Shooting::FuzzyLine(look.soldier) : 0.0f;
        } else if (distance <= kArmsLength) {
            visibility = 1.0f;
        } else if (hiding.behindGrass) {
            visibility = 0.0f;
        }
        Report(look, id, now, visibility, distance,
               moving ? (upright ? "walking upright" : "moving crouched") : "still", zone, hiding,
               alert ? "alert" : "idle", notice, recognise);
        return visibility;
    }
}

void InstallPerception() {
    g_trace = std::strstr(GetCommandLineA(), kTraceSwitch) != nullptr;
    if (!g_conesSet || !g_targetDistance || !g_timeOfDay || !g_currentVehicle || !g_distance ||
        !g_fov || !g_stance || !g_speed || !g_grass || !g_sampling ||
        !Hook(g_evaluate, &EvaluateDetour, &g_originalEvaluate) ||
        !FCSE::ApiPointer()->MidHook(g_conesSet.get(), &OnConesSet)) {
        FCSE::Logf("perception: the soldiers' vision was not found in this build");
        return;
    }
    if (!RedirectOperand(g_concealment, kFromOperand, &g_grassFrom) ||
        !RedirectOperand(g_concealment, kFullOperand, &g_grassFull) ||
        !RedirectOperand(g_concealment, kCombatOperand, &g_combatConcealment)) {
        FCSE::Logf("perception: grass was not found in this build");
    }
    if (!Hook(g_setFogEnd, &SetFogEndDetour, &g_originalSetFogEnd) ||
        !Hook(g_setFogAmount, &SetFogAmountDetour, &g_originalSetFogAmount)) {
        FCSE::Logf("perception: the fog was not found in this build, so fog limits no one");
    }
    if (!g_bonus || !g_bonusCategory) {
        FCSE::Logf("perception: the camo suit's bonus was not found in this build, so it hides no one");
    }
    if (g_inHighGrass) {
        g_groundHeight = reinterpret_cast<GroundHeightFn>(GetProcAddress(
            static_cast<HMODULE>(FCSE::ApiPointer()->duniaModule), "FCE_TerrainManager_GetHeightAt"));
    }
    if (g_groundHeight == nullptr) {
        FCSE::Logf("perception: the high-grass test or the terrain's height was not found in this build, so grass "
                   "between hides no one");
    }
    if (!g_camouflage || !Hook(g_treeVisibility, &TreeVisibilityDetour, &g_originalTreeVisibility) ||
        !Hook(g_treeHides, &TreeHidesDetour, &g_originalTreeHides)) {
        FCSE::Logf("perception: the RealTrees' cover was not found in this build, so bushes hide as the game has it");
    }
    if (g_trace) {
        LARGE_INTEGER frequency = {};
        QueryPerformanceFrequency(&frequency);
        g_ticksPerMicro = static_cast<double>(frequency.QuadPart) / 1e6;
    }
}
