// Watched spot: a soldier keeps his aim on where the player vanished, so a peek from that spot meets
// a settled rifle, and a player who reappears elsewhere makes him settle his aim again.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Shooting::CanShootFn g_originalCanShoot = nullptr;

    // How visible the player must be to count as seen: fire discipline's line, below which he
    // does not fire anyway.
    constexpr float kSeenVisibility = 0.4f;
    // Unseen for longer than this, the player has vanished rather than flickered.
    constexpr double kVanishedSeconds = 1.0;
    // Hardcore: how far, in radians as the soldier sees it, from where the player vanished he may
    // reappear and still be in the soldier's sights.
    constexpr float kWatchedAngle = 0.1f;

    // Where each soldier last saw the player, and when, by game clock.
    struct Sighting {
        float position[3] = {};
        double at = -1.0;
    };
    std::unordered_map<const void*, Sighting> g_sightings;

    float Distance(const float a[3], const float b[3]) {
        float squared = 0.0f;
        for (int i = 0; i < 3; ++i) {
            squared += (a[i] - b[i]) * (a[i] - b[i]);
        }
        return std::sqrt(squared);
    }

    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        if (!Shooting::AimsAtPlayer(shooting) ||
            Shooting::Visibility(shooting) < kSeenVisibility) {
            return true;
        }
        void* shooter = FCSE::ApiPointer()->EntityData->EntityOf(Shooting::ShooterPawn(shooting));
        void* player = Game::PlayerEntity();
        if (shooter == nullptr || player == nullptr) {
            return true;
        }
        float from[3];
        float to[3];
        Game::Position(shooter, from);
        Game::Position(player, to);

        const double now = Game::Now();
        Sighting& last = Shooting::Remember(g_sightings, shooting);
        if (last.at >= 0.0 && now - last.at > kVanishedSeconds) {
            const float range = std::max(Distance(from, to), 1.0f);
            const float moved = Distance(last.position, to);
            if (moved / range > kWatchedAngle) {
                Shooting::RestartAim(shooting);
                FCSE::Logf("watched spot: the player reappeared %.1f m from where he vanished, "
                           "at %.1f m, aim restarts",
                           moved, range);
            }
        }
        for (int i = 0; i < 3; ++i) {
            last.position[i] = to[i];
        }
        last.at = now;
        return true;
    }
}

void InstallWatchedSpot() {
    if (!Shooting::Found() || !Game::ClockFound() ||
        !Hook(Shooting::CanShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("watched spot: the soldiers' shoot task was not found in this build");
    }
}
