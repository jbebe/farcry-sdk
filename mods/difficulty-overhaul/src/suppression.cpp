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
    using CanShootFn = bool(__fastcall*)(void* task);
    // __thiscall with two stack arguments, which a free function spells __fastcall.
    using OnBulletShotFn = void(__fastcall*)(void* processor, void* unused, const uint8_t* stim,
                                             bool hit);

    // CTaskShoot::CanShoot, and CSensoryStimProcessor::OnBulletShot, which marks a soldier under
    // fire for a bullet that hits him, lands near him or passes close to his head.
    FCSE::Relocation<CanShootFn> g_canShoot{FCSE::Uplay(0x009792F0)};
    FCSE::Relocation<OnBulletShotFn> g_onBulletShot{FCSE::Uplay(0x00A24770)};
    CanShootFn g_originalCanShoot = nullptr;
    OnBulletShotFn g_originalOnBulletShot = nullptr;

    // CSensoryStimProcessor's agent.
    constexpr ptrdiff_t kProcessorAgent = 0x04;

    // Hardcore: how long a bullet keeps him from firing at the player.
    constexpr double kPinnedSeconds = 1.0;

    // Until when each shooting system is pinned down, by game clock.
    std::unordered_map<const void*, double> g_pinnedUntil;
    constexpr size_t kMaxPinned = 512;

    bool Pinned(const void* shooting) {
        const auto pinned = g_pinnedUntil.find(shooting);
        return pinned != g_pinnedUntil.end() && Game::Now() < pinned->second;
    }

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
        if (!Pinned(shooting)) {
            FCSE::Logf("suppressed: %s at %.1f m", hit ? "hit" : "near miss",
                       Shooting::TargetDistance(shooting));
        }
        // Forget every soldier once there are more than kMaxPinned.
        if (g_pinnedUntil.size() > kMaxPinned) {
            g_pinnedUntil.clear();
        }
        g_pinnedUntil[shooting] = Game::Now() + kPinnedSeconds;
        Shooting::RestartAim(shooting);
    }

    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        return !Pinned(shooting) || !Shooting::AimsAtPlayer(shooting);
    }
}

void InstallSuppression() {
    if (!Shooting::Found() || !Game::ClockFound() ||
        !Hook(g_onBulletShot, &OnBulletShotDetour, &g_originalOnBulletShot) ||
        !Hook(g_canShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("suppression: the soldiers' bullet senses were not found in this build");
    }
}
