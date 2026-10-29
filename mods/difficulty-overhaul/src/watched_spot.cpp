// Watched spot: a soldier keeps his aim on where the player vanished, so a peek from that spot meets
// a settled rifle, and a player who reappears elsewhere makes him settle his aim again.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Shooting::TaskCheckFn g_originalCanShoot = nullptr;

    // Hardcore: how visible the player must be for a soldier to see him clearly.
    constexpr float kClearVisibility = 0.4f;
    // Unseen for longer than this, the player has vanished rather than flickered.
    constexpr double kVanishedSeconds = 1.0;
    // Two sightings that began closer together than this are the same one.
    constexpr double kSameSightingSeconds = 0.25;
    // Hardcore: how far, in radians as the soldier sees it, from where the player vanished he may
    // reappear and still be in the soldier's sights.
    constexpr float kWatchedAngle = 0.1f;

    // Where and when, by game clock, each soldier last saw the player, and when that unbroken
    // sighting began.
    struct Sighting {
        float position[3] = {};
        double at = -1.0;
        double since = -1.0;
    };
    std::unordered_map<const void*, Sighting> g_sightings;

    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        if (!Shooting::AimsAtPlayer(shooting) ||
            Shooting::Visibility(shooting) < kClearVisibility) {
            return true;
        }
        const float sight = Shooting::SightSeconds(shooting);
        if (sight < 0.0f) {
            return true;
        }
        float from[3];
        float to[3];
        if (!Shooting::Positions(shooting, from, to)) {
            return true;
        }

        const double now = Game::Now();
        const double since = now - sight;
        Sighting& last = Remember(g_sightings, shooting);
        // A sighting that began after the last one we saw, with a gap long enough to vanish in.
        const double unseen = since - last.at;
        if (last.at >= 0.0 && since > last.since + kSameSightingSeconds &&
            unseen > kVanishedSeconds) {
            const float range = std::max(Game::Distance(from, to), 1.0f);
            const float moved = Game::Distance(last.position, to);
            if (moved / range > kWatchedAngle) {
                Shooting::RestartAim(shooting);
                FCSE::Logf("watched spot: the player reappeared %.1f m from where he vanished "
                           "after at most %.1f s, at %.1f m, aim restarts",
                           moved, unseen, range);
            }
        }
        std::copy_n(to, 3, last.position);
        last.at = now;
        last.since = since;
        return true;
    }
}

void InstallWatchedSpot() {
    if (!Shooting::Found() || !Game::ClockFound() ||
        !Hook(Shooting::CanShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("watched spot: the soldiers' shoot task was not found in this build");
    }
}
