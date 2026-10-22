// The AI hit model against the player: aim that settles with distance, a chance that means what it
// says, recoil in place of forced misses, no certain hits, aim at the body, and a chance that falls
// with how well he sees the player and how fast the player crosses his aim.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    using ChanceFn = float(__fastcall*)(void* shooting);
    using SetupAimingDotFn = void(__fastcall*)(void* shooting);
    // __thiscall with three stack arguments, which a free function spells __fastcall.
    using ShootAtFn = int(__fastcall*)(void* shooting, void* unused, float* position, bool* head,
                                       bool* torso);

    // CShootingSystem's GetChanceOfSuccess, GetTimerToMissFactor, GenerateShootAtPosition and
    // SetupAimingDot.
    FCSE::Relocation<ChanceFn> g_getChance{FCSE::Uplay(0x00A19EE0)};
    FCSE::Relocation<ChanceFn> g_getTimerFactor{FCSE::Uplay(0x00A19A60)};
    FCSE::Relocation<ShootAtFn> g_shootAt{FCSE::Uplay(0x00A1A110)};
    FCSE::Relocation<SetupAimingDotFn> g_setupAimingDot{FCSE::Uplay(0x00A193E0)};
    ChanceFn g_originalGetChance = nullptr;
    ChanceFn g_originalGetTimerFactor = nullptr;
    ShootAtFn g_originalShootAt = nullptr;

    // CShootingSystem: the part its aim dot is on (-1 until set up), the part to set the next dot up
    // on, the reaction CLifeTimer's lifetime, and whether the last shot hit.
    constexpr ptrdiff_t kDotPart = 0x1C;
    constexpr ptrdiff_t kAimPart = 0x34;
    constexpr ptrdiff_t kReactionLifetime = 0xBC;
    constexpr ptrdiff_t kLastShotHit = 0x108;
    constexpr int kHeadPart = 3;
    constexpr int kTorsoPart = 5;

    // The effective data in a CPawn's data block, and the velocity in it.
    constexpr ptrdiff_t kDataEffective = 0x2D0;
    constexpr ptrdiff_t kEffectiveVelocity = 0xB0;

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
    // Hardcore: how visible the player must be to him for a full chance.
    constexpr float kFullVisibility = 0.6f;
    // Hardcore: what each radian per second the player crosses his aim at costs the chance.
    constexpr float kCrossingLoss = 1.0f;

    // When each shooting system last fired at the player, by game clock.
    std::unordered_map<const void*, double> g_lastShot;
    constexpr size_t kMaxShooters = 512;

    // The shot at the player being rolled, with recoil NaN between such shots.
    struct Shot {
        float recoil = NAN;
        float sighting = 1.0f;
        float crossing = 1.0f;
        float chance = NAN;
    };
    Shot g_shot;

    // How settled his aim is, 0 to 1, from when the reaction timer started.
    float Settled(const void* shooting) {
        const float elapsed = static_cast<float>(Game::Now()) - Shooting::AimStart(shooting);
        const float distance = Shooting::TargetDistance(shooting);
        return std::clamp(elapsed / (kSettleSeconds + kSettleSecondsPerMetre * distance), 0.0f,
                          1.0f);
    }

    float Recoil(const void* shooting) {
        const auto last = g_lastShot.find(shooting);
        if (last == g_lastShot.end()) {
            return 1.0f;
        }
        const double since = Game::Now() - last->second;
        return 1.0f - kRecoilLoss * static_cast<float>(std::exp(-since / kRecoilRecoverySeconds));
    }

    float Sighting(void* shooting) {
        return std::min(Shooting::Visibility(shooting) / kFullVisibility, 1.0f);
    }

    // How hard the player's movement across his line of fire makes the shot, as a factor.
    float Crossing(void* shooting) {
        const FCSE_EntityDataAPI* data = FCSE::ApiPointer()->EntityData;
        void* playerPawn = Shooting::Pawn(Shooting::Target(shooting));
        void* shooter = data->EntityOf(Shooting::ShooterPawn(shooting));
        void* player = data->EntityOf(playerPawn);
        if (shooter == nullptr || player == nullptr) {
            return 1.0f;
        }
        float from[3];
        float to[3];
        Game::Position(shooter, from);
        Game::Position(player, to);
        const uint8_t* effective = Shooting::PawnData(playerPawn) + kDataEffective;
        float line = 0.0f;
        float speed = 0.0f;
        float along = 0.0f;
        for (int i = 0; i < 3; ++i) {
            const float l = to[i] - from[i];
            const float v = Read<float>(effective, kEffectiveVelocity + i * sizeof(float));
            line += l * l;
            speed += v * v;
            along += v * l;
        }
        if (line < 1.0f) {
            return 1.0f;
        }
        // The angle he must swing through per second: |v x l| / |l|^2.
        const float rate = std::sqrt(std::max(speed * line - along * along, 0.0f)) / line;
        return 1.0f / (1.0f + kCrossingLoss * rate);
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
        if (Read<float>(shooting, kReactionLifetime) == -1.0f ||
            !Shooting::AimsAtPlayer(shooting)) {
            return g_originalGetTimerFactor(shooting);
        }
        return Settled(shooting);
    }

    float __fastcall GetChanceDetour(void* shooting) {
        float chance = g_originalGetChance(shooting);
        if (std::isnan(g_shot.recoil)) {
            return chance;
        }
        // A certain hit is point blank, which the reaction timer does not reach.
        if (chance >= 1.0f) {
            chance *= Settled(shooting);
        }
        g_shot.chance =
            std::min(chance, kMaxChance) * g_shot.recoil * g_shot.sighting * g_shot.crossing;
        return RollChance(g_shot.chance);
    }

    int __fastcall ShootAtDetour(void* shooting, void* unused, float* position, bool* head,
                                 bool* torso) {
        if (!Shooting::AimsAtPlayer(shooting)) {
            return g_originalShootAt(shooting, unused, position, head, torso);
        }

        // No streak of forced misses after a hit.
        Write<bool>(shooting, kLastShotHit, false);
        // Each shot is aimed at the head or the torso by kHeadAimShare.
        const int part = std::rand() < kHeadAimShare * RAND_MAX ? kHeadPart : kTorsoPart;
        if (Read<int>(shooting, kDotPart) != part) {
            Write(shooting, kAimPart, part);
            g_setupAimingDot(shooting);
        }

        g_shot = {Recoil(shooting), Sighting(shooting), Crossing(shooting)};
        const int result = g_originalShootAt(shooting, unused, position, head, torso);
        FCSE::Logf("shot at player: chance %.2f (recoil %.2f, sighting %.2f, crossing %.2f) at "
                   "%.1f m, %s",
                   g_shot.chance, g_shot.recoil, g_shot.sighting, g_shot.crossing,
                   Shooting::TargetDistance(shooting),
                   Read<bool>(shooting, kLastShotHit) ? "hit" : "miss");
        g_shot = {};
        // Forget every shooter once there are more than kMaxShooters.
        if (g_lastShot.size() > kMaxShooters) {
            g_lastShot.clear();
        }
        g_lastShot[shooting] = Game::Now();
        return result;
    }
}

void InstallHitModel() {
    if (!Shooting::Found() || !g_setupAimingDot || !Game::ClockFound() ||
        !Hook(g_getTimerFactor, &GetTimerFactorDetour, &g_originalGetTimerFactor) ||
        !Hook(g_getChance, &GetChanceDetour, &g_originalGetChance) ||
        !Hook(g_shootAt, &ShootAtDetour, &g_originalShootAt)) {
        FCSE::Logf("hit model: the AI shooting system was not found in this build");
    }
}
