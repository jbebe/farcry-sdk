// Suppression: a soldier the player's bullets hit or pass close to stops firing at the player for a
// moment, and his aim has to settle again before it is good.
#include "engine/damage.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <cstdint>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with two stack arguments, which a free function spells __fastcall.
    using OnBulletShotFn = void(__fastcall*)(void* processor, void* unused, const uint8_t* stim,
                                             bool hit);

    // CSensoryStimProcessor::OnBulletShot, which marks a soldier under fire for a bullet that hits
    // him, lands near him or passes close to his head.
    FCSE::Relocation<OnBulletShotFn> g_onBulletShot{FCSE::Uplay(0x00A24770)};
    OnBulletShotFn g_originalOnBulletShot = nullptr;
    Shooting::CanShootFn g_originalCanShoot = nullptr;

    // CSensoryStimProcessor's agent.
    constexpr ptrdiff_t kProcessorAgent = 0x04;

    // Hardcore: how long a bullet keeps him from firing at the player.
    constexpr double kPinnedSeconds = 1.0;

    // Until when each pinned-down shooting system stays pinned, by game clock.
    std::unordered_map<const void*, double> g_pinnedUntil;

    void __fastcall OnBulletShotDetour(void* processor, void* unused, const uint8_t* stim, bool hit) {
        g_originalOnBulletShot(processor, unused, stim, hit);
        if (!Damage::SentByPlayer(stim)) {
            return;
        }
        void* agent = Read<void*>(processor, kProcessorAgent);
        if (agent == nullptr || Shooting::IsPlayer(agent)) {
            return;
        }
        void* shooting = Shooting::OfAgent(agent);
        if (!Shooting::AimsAtPlayer(shooting)) {
            return;
        }
        const double now = Game::Now();
        double& pinnedUntil = Remember(g_pinnedUntil, shooting);
        if (now >= pinnedUntil) {
            FCSE::Logf("suppressed: %s at %.1f m", hit ? "hit" : "near miss",
                       Shooting::TargetDistance(shooting));
        }
        pinnedUntil = now + kPinnedSeconds;
        Shooting::RestartAim(shooting);
    }

    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        const auto pinned = g_pinnedUntil.find(shooting);
        if (pinned == g_pinnedUntil.end()) {
            return true;
        }
        if (Game::Now() >= pinned->second) {
            g_pinnedUntil.erase(pinned);
            return true;
        }
        return !Shooting::AimsAtPlayer(shooting);
    }
}

void InstallSuppression() {
    if (!Shooting::Found() || !Game::ClockFound() ||
        !Hook(g_onBulletShot, &OnBulletShotDetour, &g_originalOnBulletShot) ||
        !Hook(Shooting::CanShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("suppression: the soldiers' bullet senses were not found in this build");
    }
}
