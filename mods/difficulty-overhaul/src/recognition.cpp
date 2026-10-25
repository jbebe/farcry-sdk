// Recognition: an alerted soldier who only has a faint sighting of the player keeps looking until he
// sees the player clearly, where the game reports the faint sighting and stops looking until his
// next plan starts.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with the frame's seconds on the stack, which a free function spells __fastcall.
    using FindThreatFn = int(__fastcall*)(void* task, void* unused, float seconds);

    // CTaskFindVisualThreat::DoRun: sorts the agents he sees into clear and faint threats, and
    // succeeds, so his plan reports them and moves on, once either list holds anyone.
    FCSE::Relocation<FindThreatFn> g_findThreat{FCSE::Uplay(0x0099A050)};
    FindThreatFn g_originalFindThreat = nullptr;

    // The task's agent; the agent's army-member state, and his faint, clear and other threat lists,
    // each a pointer and a count of entries holding the agent seen.
    constexpr ptrdiff_t kTaskAgent = 0x34;
    constexpr ptrdiff_t kAgentState = 0x274;
    constexpr ptrdiff_t kFaintThreats = 0x214;
    constexpr ptrdiff_t kFaintCount = 0x218;
    constexpr ptrdiff_t kClearCount = 0x224;
    constexpr ptrdiff_t kOtherCount = 0x230;
    constexpr ptrdiff_t kThreatSize = 0x34;
    constexpr ptrdiff_t kThreatAgent = 0x30;
    constexpr int kAlert = 2;

    // Hardcore: how far the player must move from where an alerted soldier last noticed him for a
    // faint sighting to send him looking somewhere new.
    constexpr float kRenoticeMetres = 3.0f;

    // Where each alerted soldier last reported a faint sighting of the player.
    struct Noticed {
        float at[3] = {};
        bool reported = false;
    };
    std::unordered_map<const void*, Noticed> g_noticed;

    bool FaintlySeesPlayer(const uint8_t* agent) {
        const auto* threats = Read<const uint8_t*>(agent, kFaintThreats);
        const int count = Read<int>(agent, kFaintCount);
        for (int i = 0; i < count; ++i) {
            void* seen = Read<void*>(threats + i * kThreatSize, kThreatAgent);
            if (seen != nullptr && Shooting::IsPlayer(seen)) {
                return true;
            }
        }
        return false;
    }

    int __fastcall FindThreatDetour(void* task, void* unused, float seconds) {
        const int found = g_originalFindThreat(task, unused, seconds);
        const auto* agent = Read<const uint8_t*>(task, kTaskAgent);
        if (found == 0 || Read<int>(agent, kAgentState) != kAlert ||
            Read<int>(agent, kClearCount) != 0 || Read<int>(agent, kOtherCount) != 0 ||
            !FaintlySeesPlayer(agent)) {
            return found;
        }
        void* player = Game::PlayerEntity();
        if (player == nullptr) {
            return found;
        }
        float now[3];
        Game::Position(player, now);
        Noticed& last = Shooting::Remember(g_noticed, agent);
        float squared = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            squared += (now[axis] - last.at[axis]) * (now[axis] - last.at[axis]);
        }
        if (last.reported && std::sqrt(squared) < kRenoticeMetres) {
            return 0;
        }
        for (int axis = 0; axis < 3; ++axis) {
            last.at[axis] = now[axis];
        }
        last.reported = true;
        return found;
    }
}

void InstallRecognition() {
    if (!Hook(g_findThreat, &FindThreatDetour, &g_originalFindThreat)) {
        FCSE::Logf("recognition: the soldiers' threat search was not found in this build");
    }
}
