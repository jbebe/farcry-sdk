// The AI hit model against the player: aim that settles with distance, a chance that means what it
// says, recoil in place of forced misses, no certain hits, and aim at the body rather than the head.
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    using ChanceFn = float(__fastcall*)(void* shooting);
    using GetTargetFn = void*(__fastcall*)(void* shooting);
    using IsAPlayerFn = bool(__fastcall*)(void* agent);
    // __thiscall with three stack arguments, which a free function spells __fastcall.
    using ShootAtFn = int(__fastcall*)(void* shooting, void* unused, float* position, bool* head,
                                       bool* torso);

    // CShootingSystem's GetChanceOfSuccess, GetTimerToMissFactor, GenerateShootAtPosition and
    // GetTarget, and the game clock.
    FCSE::Relocation<ChanceFn> g_getChance{FCSE::Uplay(0x00A19EE0)};
    FCSE::Relocation<ChanceFn> g_getTimerFactor{FCSE::Uplay(0x00A19A60)};
    FCSE::Relocation<ShootAtFn> g_shootAt{FCSE::Uplay(0x00A1A110)};
    FCSE::Relocation<GetTargetFn> g_getTarget{FCSE::Uplay(0x00A19370)};
    FCSE::Relocation<void**> g_clock{FCSE::Uplay(0x01606360)};
    ChanceFn g_originalGetChance = nullptr;
    ChanceFn g_originalGetTimerFactor = nullptr;
    ShootAtFn g_originalShootAt = nullptr;

    // CShootingSystem: the aim dot's bone (-1 until set up), the part it aims at, the reaction
    // CLifeTimer (lifetime, then start time), the target distance, and whether the last shot hit.
    constexpr ptrdiff_t kAimBone = 0x1C;
    constexpr ptrdiff_t kAimPart = 0x34;
    constexpr ptrdiff_t kReactionLifetime = 0xBC;
    constexpr ptrdiff_t kReactionStart = 0xC0;
    constexpr ptrdiff_t kTargetDistance = 0xE8;
    constexpr ptrdiff_t kLastShotHit = 0x108;
    constexpr int kHeadPart = 3;
    constexpr int kTorsoPart = 5;

    constexpr ptrdiff_t kClockSeconds = 0x30;
    constexpr ptrdiff_t kIsAPlayerSlot = 0xBC;

    // Hardcore: seconds for aim to settle after acquiring the player, at 0 m and per metre.
    constexpr float kSettleSeconds = 0.6f;
    constexpr float kSettleSecondsPerMetre = 0.02f;
    // Hardcore: the best chance any one shot has.
    constexpr float kMaxChance = 0.85f;
    // Hardcore: how much of the chance a shot's recoil takes from the next, and how fast it returns.
    constexpr float kRecoilLoss = 0.6f;
    constexpr float kRecoilRecoverySeconds = 0.25f;
    // Hardcore: the share of aim dots on the head rather than the torso.
    constexpr float kHeadAimShare = 0.15f;

    // When each shooting system last fired at the player, by game clock.
    std::unordered_map<const void*, double> g_lastShot;
    constexpr size_t kMaxShooters = 512;
    // Set while GenerateShootAtPosition rolls a shot at the player, and the chance that shot had.
    bool g_rolling = false;
    float g_shotChance = 0.0f;

    double Now() { return Read<double>(*g_clock.get(), kClockSeconds); }

    bool AimsAtPlayer(void* shooting) {
        void* target = g_getTarget(shooting);
        return target != nullptr &&
               Read<IsAPlayerFn>(Read<const void*>(target, 0), kIsAPlayerSlot)(target);
    }

    // How settled his aim is, 0 to 1, from when the reaction timer started.
    float Settled(const void* shooting) {
        const float elapsed = static_cast<float>(Now()) - Read<float>(shooting, kReactionStart);
        const float distance = Read<float>(shooting, kTargetDistance);
        return std::clamp(elapsed / (kSettleSeconds + kSettleSecondsPerMetre * distance), 0.0f,
                          1.0f);
    }

    float Recoil(const void* shooting) {
        const auto last = g_lastShot.find(shooting);
        if (last == g_lastShot.end()) {
            return 1.0f;
        }
        const double since = Now() - last->second;
        return 1.0f - kRecoilLoss * static_cast<float>(std::exp(-since / kRecoilRecoverySeconds));
    }

    // The chance to give a roll against the mean of three uniforms so that it succeeds with `p`.
    float RollChance(float p) {
        const auto cdf = [](float x) {
            const float s = 3.0f * x;
            if (s <= 1.0f) {
                return s * s * s / 6.0f;
            }
            if (s <= 2.0f) {
                return (-2.0f * s * s * s + 9.0f * s * s - 9.0f * s + 3.0f) / 6.0f;
            }
            return 1.0f - (3.0f - s) * (3.0f - s) * (3.0f - s) / 6.0f;
        };
        float low = 0.0f;
        float high = 1.0f;
        for (int i = 0; i < 24; ++i) {
            const float mid = (low + high) / 2.0f;
            (cdf(mid) < p ? low : high) = mid;
        }
        return high;
    }

    float __fastcall GetTimerFactorDetour(void* shooting) {
        if (!AimsAtPlayer(shooting) || Read<float>(shooting, kReactionLifetime) == -1.0f) {
            return g_originalGetTimerFactor(shooting);
        }
        return Settled(shooting);
    }

    float __fastcall GetChanceDetour(void* shooting) {
        float chance = g_originalGetChance(shooting);
        if (!g_rolling) {
            return chance;
        }
        // Point blank answers a certain hit and skips the reaction timer.
        if (chance >= 1.0f) {
            chance *= Settled(shooting);
        }
        g_shotChance = std::min(chance, kMaxChance) * Recoil(shooting);
        return RollChance(g_shotChance);
    }

    int __fastcall ShootAtDetour(void* shooting, void* unused, float* position, bool* head,
                                 bool* torso) {
        if (!AimsAtPlayer(shooting)) {
            return g_originalShootAt(shooting, unused, position, head, torso);
        }

        auto* system = static_cast<uint8_t*>(shooting);
        // No streak of forced misses after a hit.
        system[kLastShotHit] = 0;
        // A dot about to be set up aims at the part chosen here.
        if (Read<int>(system, kAimBone) == -1) {
            const bool aimHead = std::rand() < kHeadAimShare * RAND_MAX;
            *reinterpret_cast<int*>(system + kAimPart) = aimHead ? kHeadPart : kTorsoPart;
        }

        g_rolling = true;
        g_shotChance = NAN;
        const float recoil = Recoil(shooting);
        const int result = g_originalShootAt(shooting, unused, position, head, torso);
        g_rolling = false;
        FCSE::Logf("shot at player: chance %.2f (recoil %.2f) at %.1f m, %s", g_shotChance, recoil,
                   Read<float>(system, kTargetDistance), system[kLastShotHit] ? "hit" : "miss");
        // Shooting systems die with their soldiers; an old one's time only means a stale recoil.
        if (g_lastShot.size() > kMaxShooters) {
            g_lastShot.clear();
        }
        g_lastShot[shooting] = Now();
        return result;
    }
}

void InstallHitModel() {
    if (!g_getTarget || !g_clock ||
        !Hook(g_getTimerFactor, &GetTimerFactorDetour, &g_originalGetTimerFactor) ||
        !Hook(g_getChance, &GetChanceDetour, &g_originalGetChance) ||
        !Hook(g_shootAt, &ShootAtDetour, &g_originalShootAt)) {
        FCSE::Logf("hit model: the AI shooting system was not found in this build");
    }
}
