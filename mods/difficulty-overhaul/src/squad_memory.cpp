// Squad memory: a squad keeps the player as its target, at the spot it last saw him, for as long as
// he stays near that spot; only slipping away unseen lets it forget him.
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with one stack argument, which a free function spells __fastcall.
    using UpdateTargetListFn = void(__fastcall*)(void* lieutenant, void* unused, bool* noTargets);

    // CDispatcherSquadLieutenant::UpdateTargetList, which drops a seen target 10 s plus the
    // camping score after the squad last saw it.
    FCSE::Relocation<UpdateTargetListFn> g_updateTargetList{FCSE::Uplay(0x0093A2A0)};
    UpdateTargetListFn g_originalUpdateTargetList = nullptr;

    // CDispatcherSquadLieutenant's army and camping score; the army's list of targets it has seen;
    // an entry's proxy and the position it was last seen at.
    constexpr ptrdiff_t kLieutenantArmy = 0x130;
    constexpr ptrdiff_t kLieutenantCampingScore = 0x230;
    constexpr ptrdiff_t kArmySeenTargets = 0xBC;
    constexpr ptrdiff_t kArmySeenTargetCount = 0xC0;
    constexpr ptrdiff_t kSeenTargetSize = 0x34;
    constexpr ptrdiff_t kSeenTargetProxy = 0x00;
    constexpr ptrdiff_t kSeenTargetPosition = 0x04;

    // Hardcore: how far from where the squad last saw him the player must get to be forgotten.
    constexpr float kHoldRadius = 15.0f;
    // A camping score no target outlasts.
    constexpr float kForever = 1.0e9f;

    // How far the player now is from where the lieutenant's squad last saw him, or a negative
    // number when the squad has not seen him.
    float PlayerDistanceFromLastSeen(void* lieutenant) {
        const auto* army = Read<const uint8_t*>(lieutenant, kLieutenantArmy);
        if (army == nullptr) {
            return -1.0f;
        }
        const auto* targets = Read<const uint8_t*>(army, kArmySeenTargets);
        const int count = Read<int>(army, kArmySeenTargetCount);
        for (int i = 0; i < count; ++i) {
            const uint8_t* target = targets + i * kSeenTargetSize;
            void* entity = Game::ProxyEntity(Read<const void*>(target, kSeenTargetProxy));
            if (!Game::IsPlayerEntity(entity)) {
                continue;
            }
            float now[3];
            Game::Position(entity, now);
            float squared = 0.0f;
            for (int axis = 0; axis < 3; ++axis) {
                const float d =
                    now[axis] - Read<float>(target, kSeenTargetPosition + axis * sizeof(float));
                squared += d * d;
            }
            return std::sqrt(squared);
        }
        return -1.0f;
    }

    // While the player is near where they last saw him, the camping score is raised past any
    // timeout for this one pass, which also keeps the squad's other seen targets.
    void __fastcall UpdateTargetListDetour(void* lieutenant, void* unused, bool* noTargets) {
        const float distance = PlayerDistanceFromLastSeen(lieutenant);
        if (distance < 0.0f || distance > kHoldRadius) {
            g_originalUpdateTargetList(lieutenant, unused, noTargets);
            return;
        }
        const auto score = Read<float>(lieutenant, kLieutenantCampingScore);
        Write(lieutenant, kLieutenantCampingScore, kForever);
        g_originalUpdateTargetList(lieutenant, unused, noTargets);
        Write(lieutenant, kLieutenantCampingScore, score);
        FCSE::Logf("squad memory: the player is %.1f m from where they last saw him, still hunted",
                   distance);
    }
}

void InstallSquadMemory() {
    if (!Hook(g_updateTargetList, &UpdateTargetListDetour, &g_originalUpdateTargetList)) {
        FCSE::Logf("squad memory: the squads' target list was not found in this build");
    }
}
