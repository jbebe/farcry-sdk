// The AI hit model against the player: fire held until he sees the player clearly, aim that settles
// with distance, a chance that means what it says and falls with how well he sees the player and how
// fast the player crosses his aim, recoil in place of forced misses, no certain hits, and aim at the
// body rather than the head.
#include "engine/game.h"
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

    using SetupAimingDotFn = void(__fastcall*)(void* shooting);
    using GetVisibilityFn = bool(__fastcall*)(void* sensory, void* unused, void* agent,
                                              float* visibility, bool remembered);
    using CanShootFn = bool(__fastcall*)(void* task);

    // CShootingSystem's GetChanceOfSuccess, GetTimerToMissFactor, GenerateShootAtPosition,
    // GetTarget and SetupAimingDot, CSensorySystem::GetVisibilityValue, CTaskShoot::CanShoot, and
    // the game clock.
    FCSE::Relocation<ChanceFn> g_getChance{FCSE::Uplay(0x00A19EE0)};
    FCSE::Relocation<ChanceFn> g_getTimerFactor{FCSE::Uplay(0x00A19A60)};
    FCSE::Relocation<ShootAtFn> g_shootAt{FCSE::Uplay(0x00A1A110)};
    FCSE::Relocation<GetTargetFn> g_getTarget{FCSE::Uplay(0x00A19370)};
    FCSE::Relocation<SetupAimingDotFn> g_setupAimingDot{FCSE::Uplay(0x00A193E0)};
    FCSE::Relocation<GetVisibilityFn> g_getVisibility{FCSE::Uplay(0x00A021A0)};
    FCSE::Relocation<CanShootFn> g_canShoot{FCSE::Uplay(0x009792F0)};
    FCSE::Relocation<void**> g_clock{FCSE::Uplay(0x01606360)};
    ChanceFn g_originalGetChance = nullptr;
    ChanceFn g_originalGetTimerFactor = nullptr;
    ShootAtFn g_originalShootAt = nullptr;
    CanShootFn g_originalCanShoot = nullptr;

    // CShootingSystem: the shooter's CPawnAgent, the part its aim dot is on (-1 until set up), the
    // part to set the next dot up on, the reaction CLifeTimer (lifetime, then start time), the
    // target distance, and whether the last shot hit.
    constexpr ptrdiff_t kShooter = 0x00;
    constexpr ptrdiff_t kDotPart = 0x1C;
    constexpr ptrdiff_t kAimPart = 0x34;
    constexpr ptrdiff_t kReactionLifetime = 0xBC;
    constexpr ptrdiff_t kReactionStart = 0xC0;
    constexpr ptrdiff_t kTargetDistance = 0xE8;
    constexpr ptrdiff_t kLastShotHit = 0x108;
    constexpr int kHeadPart = 3;
    constexpr int kTorsoPart = 5;

    constexpr ptrdiff_t kClockSeconds = 0x30;
    constexpr ptrdiff_t kIsAPlayerSlot = 0xBC;
    // CPawnAgent's CSensorySystem and CShootingSystem, held in place, and its CPawn; CTaskShoot's
    // agent.
    constexpr ptrdiff_t kAgentSenses = 0x28C;
    constexpr ptrdiff_t kAgentShooting = 0x4A0;
    constexpr ptrdiff_t kAgentPawn = 0x268;
    constexpr ptrdiff_t kTaskAgent = 0x34;
    // CPawn's data block, its effective data within it, and the velocity there; an entity's position.
    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataEffective = 0x2D0;
    constexpr ptrdiff_t kEffectiveVelocity = 0xB0;
    constexpr ptrdiff_t kEntityPosition = 0x60;

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
    // Hardcore: how visible the player must be to the shooter for him to fire at all, and for a
    // full chance.
    constexpr float kFireVisibility = 0.4f;
    constexpr float kFullVisibility = 0.6f;
    // Hardcore: how much the angle the player crosses per second (radians) costs the chance, as
    // 1 / (1 + kCrossingLoss * rate).
    constexpr float kCrossingLoss = 1.0f;

    // When each shooting system last fired at the player, by game clock.
    std::unordered_map<const void*, double> g_lastShot;
    constexpr size_t kMaxShooters = 512;

    // The shot at the player being rolled: its recoil, sighting and crossing factors, NaN between
    // such shots, and the chance it was given.
    struct Shot {
        float recoil = NAN;
        float sighting = NAN;
        float crossing = NAN;
        float chance = NAN;
    };
    Shot g_shot;

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

    // How hard the player's movement across his line of fire makes the shot, as a factor.
    float Crossing(void* shooting) {
        auto* shooter = FCSE::ApiPointer()->EntityData->EntityOf(
            Read<void*>(Read<void*>(shooting, kShooter), kAgentPawn));
        void* player = Game::PlayerEntity();
        if (shooter == nullptr || player == nullptr) {
            return 1.0f;
        }
        auto* effective = Read<uint8_t*>(Read<void*>(g_getTarget(shooting), kAgentPawn), kPawnData) +
                          kDataEffective;
        float line[3];
        float velocity[3];
        for (int i = 0; i < 3; ++i) {
            const ptrdiff_t at = i * sizeof(float);
            line[i] = Read<float>(player, kEntityPosition + at) -
                      Read<float>(shooter, kEntityPosition + at);
            velocity[i] = Read<float>(effective, kEffectiveVelocity + at);
        }
        const float distance = std::hypot(line[0], line[1], line[2]);
        if (distance < 1.0f) {
            return 1.0f;
        }
        const float along = (velocity[0] * line[0] + velocity[1] * line[1] + velocity[2] * line[2]) /
                            distance;
        const float speed = std::hypot(velocity[0], velocity[1], velocity[2]);
        const float across = std::sqrt(std::max(speed * speed - along * along, 0.0f));
        return 1.0f / (1.0f + kCrossingLoss * across / distance);
    }

    // How visible his target is to him now, 0 when he does not see it.
    float Visibility(void* shooting) {
        auto* senses = Read<uint8_t*>(shooting, kShooter) + kAgentSenses;
        float visibility = 0.0f;
        g_getVisibility(senses, nullptr, g_getTarget(shooting), &visibility, false);
        return visibility;
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
        if (Read<float>(shooting, kReactionLifetime) == -1.0f || !AimsAtPlayer(shooting)) {
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
        if (!AimsAtPlayer(shooting)) {
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

        g_shot = {Recoil(shooting), std::min(Visibility(shooting) / kFullVisibility, 1.0f),
                  Crossing(shooting)};
        const int result = g_originalShootAt(shooting, unused, position, head, torso);
        FCSE::Logf("shot at player: chance %.2f (recoil %.2f, sighting %.2f, crossing %.2f) at "
                   "%.1f m, %s",
                   g_shot.chance, g_shot.recoil, g_shot.sighting, g_shot.crossing,
                   Read<float>(shooting, kTargetDistance),
                   Read<bool>(shooting, kLastShotHit) ? "hit" : "miss");
        g_shot = {};
        // Forget every shooter once there are more than kMaxShooters.
        if (g_lastShot.size() > kMaxShooters) {
            g_lastShot.clear();
        }
        g_lastShot[shooting] = Now();
        return result;
    }

    // He holds his fire at the player until he sees the player clearly.
    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Read<uint8_t*>(task, kTaskAgent) + kAgentShooting;
        return !AimsAtPlayer(shooting) || Visibility(shooting) >= kFireVisibility;
    }
}

void InstallHitModel() {
    if (!g_getTarget || !g_setupAimingDot || !g_getVisibility || !g_clock ||
        !Hook(g_getTimerFactor, &GetTimerFactorDetour, &g_originalGetTimerFactor) ||
        !Hook(g_getChance, &GetChanceDetour, &g_originalGetChance) ||
        !Hook(g_shootAt, &ShootAtDetour, &g_originalShootAt) ||
        !Hook(g_canShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("hit model: the AI shooting system was not found in this build");
    }
}
