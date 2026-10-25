// Squad memory: a squad keeps the player as its target, at the spot it last saw him, for as long as
// he stays near that spot; only slipping away unseen lets it forget him.
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with one stack argument, which a free function spells __fastcall.
    using UpdateTargetListFn = void(__fastcall*)(void* lieutenant, void* unused, bool* noTargets);

    // CDispatcherSquadLieutenant::UpdateTargetList, which drops a seen target 10 s plus the
    // camping score after the squad last saw it.
    FCSE::Relocation<UpdateTargetListFn> g_updateTargetList{FCSE::Uplay(0x0093A2A0)};
    UpdateTargetListFn g_originalUpdateTargetList = nullptr;

    // CDispatcherSquadLieutenant's army and camping score; the army's list of targets it has seen;
    // an entry's proxy, the position it was last seen at and when, by game clock.
    constexpr ptrdiff_t kLieutenantArmy = 0x130;
    constexpr ptrdiff_t kLieutenantCampingScore = 0x230;
    constexpr ptrdiff_t kArmySeenTargets = 0xBC;
    constexpr ptrdiff_t kArmySeenTargetCount = 0xC0;
    constexpr ptrdiff_t kSeenTargetSize = 0x34;
    constexpr ptrdiff_t kSeenTargetProxy = 0x00;
    constexpr ptrdiff_t kSeenTargetPosition = 0x04;
    constexpr ptrdiff_t kSeenTargetTime = 0x1C;

    // How long after the last sighting the game drops a seen target, before the camping score.
    constexpr float kGameMemorySeconds = 10.0f;
    // Hardcore: how far from where the squad last saw him the player must get to be forgotten.
    constexpr float kHoldRadius = 15.0f;
    // A camping score no target outlasts.
    constexpr float kForever = 1.0e9f;

    // The sighting each lieutenant was last reported as holding on to, so each is logged once, and
    // whether its squad had the player among its seen targets on its last pass.
    std::unordered_map<const void*, float> g_reported;
    std::unordered_map<const void*, bool> g_spotted;

    // The lieutenant's entry for the player in its army's seen targets, or null.
    const uint8_t* PlayerSighting(void* lieutenant, void** player) {
        const auto* army = Read<const uint8_t*>(lieutenant, kLieutenantArmy);
        if (army == nullptr) {
            return nullptr;
        }
        const auto* targets = Read<const uint8_t*>(army, kArmySeenTargets);
        const int count = Read<int>(army, kArmySeenTargetCount);
        for (int i = 0; i < count; ++i) {
            const uint8_t* target = targets + i * kSeenTargetSize;
            *player = Game::ProxyEntity(Read<const void*>(target, kSeenTargetProxy));
            if (Game::IsPlayerEntity(*player)) {
                return target;
            }
        }
        return nullptr;
    }

    float DistanceFrom(const void* entity, const uint8_t* sighting) {
        float now[3];
        Game::Position(entity, now);
        float squared = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            const float d =
                now[axis] - Read<float>(sighting, kSeenTargetPosition + axis * sizeof(float));
            squared += d * d;
        }
        return std::sqrt(squared);
    }

    // While the player is near where they last saw him, the camping score is raised past any
    // timeout for this one pass, which also keeps the squad's other seen targets.
    void __fastcall UpdateTargetListDetour(void* lieutenant, void* unused, bool* noTargets) {
        void* player = nullptr;
        const uint8_t* sighting = PlayerSighting(lieutenant, &player);
        bool& spotted = g_spotted[lieutenant];
        if ((sighting != nullptr) != spotted) {
            spotted = sighting != nullptr;
            FCSE::Logf("squad memory: the squad %s", spotted ? "spotted the player" : "lost him");
        }
        const float distance = sighting != nullptr ? DistanceFrom(player, sighting) : -1.0f;
        if (distance < 0.0f || distance > kHoldRadius) {
            g_originalUpdateTargetList(lieutenant, unused, noTargets);
            return;
        }
        const auto score = Read<float>(lieutenant, kLieutenantCampingScore);
        const auto seenAt = Read<float>(sighting, kSeenTargetTime);
        const float unseen = static_cast<float>(Game::Now()) - seenAt;
        if (unseen > kGameMemorySeconds + score && g_reported[lieutenant] != seenAt) {
            g_reported[lieutenant] = seenAt;
            FCSE::Logf("squad memory: unseen %.0f s and %.1f m from where they last saw him, "
                       "still hunted where the game would have forgotten him",
                       unseen, distance);
        }
        Write(lieutenant, kLieutenantCampingScore, kForever);
        g_originalUpdateTargetList(lieutenant, unused, noTargets);
        Write(lieutenant, kLieutenantCampingScore, score);
    }
}

void InstallSquadMemory() {
    if (!Hook(g_updateTargetList, &UpdateTargetListDetour, &g_originalUpdateTargetList)) {
        FCSE::Logf("squad memory: the squads' target list was not found in this build");
    }
}
