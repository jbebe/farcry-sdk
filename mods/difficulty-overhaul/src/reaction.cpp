// Reaction: a soldier who spots the player before he has fired in this fight holds his fire for a
// moment, longer at range, while his aim settles; a hit that panics him ends it.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Shooting::TaskCheckFn g_originalIsAligned = nullptr;

    // Hardcore: how long he holds his fire after spotting the player, at 0 m and per metre.
    constexpr double kReactionSeconds = 1.0;
    constexpr double kReactionSecondsPerMetre = 0.025;

    // Until when each surprised soldier holds his fire, by game clock.
    std::unordered_map<const void*, double> g_holds;

    bool __fastcall IsAlignedDetour(void* task) {
        if (!g_originalIsAligned(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        if (!Shooting::AimsAtPlayer(shooting)) {
            return true;
        }
        const double now = Game::Now();
        if (!Shooting::Reacting(shooting)) {
            float from[3];
            float to[3];
            if (!Shooting::Positions(shooting, from, to)) {
                return true;
            }
            const float distance = Game::Distance(from, to);
            const double seconds = kReactionSeconds + kReactionSecondsPerMetre * distance;
            Remember(g_holds, shooting) = now + seconds;
            Shooting::StartAim(shooting);
            FCSE::Logf("reaction: spotted the player at %.1f m, holds his fire %.1f s", distance,
                       seconds);
            return false;
        }
        const auto hold = g_holds.find(shooting);
        if (hold == g_holds.end()) {
            return true;
        }
        if (now < hold->second && !Shooting::Panicking(shooting)) {
            return false;
        }
        g_holds.erase(hold);
        return true;
    }
}

void InstallReaction() {
    if (!Shooting::Found() || !Game::ClockFound() ||
        !Hook(Shooting::IsAligned, &IsAlignedDetour, &g_originalIsAligned)) {
        FCSE::Logf("reaction: the soldiers' shoot task was not found in this build");
    }
}
