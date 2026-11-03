// Reaction: a soldier who spots the player before he has fired in this fight fires at once up close
// and takes longer to make up his mind farther out; a hit that panics him ends it.
#include "engine/combat_log.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Shooting::TaskCheckFn g_originalIsAligned = nullptr;

    // Hardcore: within this range he fires as fast as he can; beyond it he holds his fire this long
    // per metre, 3 s at 50 m.
    constexpr double kInstantRange = 10.0;
    constexpr double kSecondsPerMetre = 0.075;

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
            const double seconds = std::max(distance - kInstantRange, 0.0) * kSecondsPerMetre;
            Remember(g_holds, shooting) = now + seconds;
            Shooting::StartAim(shooting);
            CombatLogf("soldier %08X reaction: spotted the player at %.1f m, holds his fire %.1f s",
                       Shooting::ShooterId(shooting), distance,
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
