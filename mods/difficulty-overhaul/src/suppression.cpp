// Suppression: the player's bullets shake a soldier's aim at the player for a moment, and one that
// hits him panics him into firing back however little he sees. Neither stops his fire.
#include "engine/damage.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with two stack arguments, which a free function spells __fastcall.
    using OnBulletShotFn = void(__fastcall*)(void* processor, void* unused, const uint8_t* stim,
                                             bool hit);

    // CSensoryStimProcessor::OnBulletShot, which marks a soldier under fire for a bullet that hits
    // him, lands near him or passes close to his head.
    FCSE::Relocation<OnBulletShotFn> g_onBulletShot{FCSE::Uplay(0x00A24770)};
    OnBulletShotFn g_originalOnBulletShot = nullptr;

    // CSensoryStimProcessor's agent.
    constexpr ptrdiff_t kProcessorAgent = 0x04;

    // Hardcore: how long a bullet shakes his aim, and what his chance keeps of it after a hit and
    // after a near miss.
    constexpr double kShakenSeconds = 1.0;
    constexpr float kHitFactor = 0.25f;
    constexpr float kNearMissFactor = 0.7f;

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
        const bool held =
            Shooting::Shake(shooting, hit ? kHitFactor : kNearMissFactor, hit, kShakenSeconds);
        if (hit || !held) {
            FCSE::Logf("suppressed: %s at %.1f m", hit ? "hit, he panics" : "near miss",
                       Shooting::TargetDistance(shooting));
        }
    }
}

void InstallSuppression() {
    if (!Shooting::Found() || !Game::ClockFound() ||
        !Hook(g_onBulletShot, &OnBulletShotDetour, &g_originalOnBulletShot)) {
        FCSE::Logf("suppression: the soldiers' bullet senses were not found in this build");
    }
}
