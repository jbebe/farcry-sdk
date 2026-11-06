// The AI hit model against the player: aim that settles with distance, a chance that means what it
// says, recoil in place of forced misses, no certain hits, aim at the body, and a chance that falls
// with how well he sees the player and how fast the player crosses his aim.
#include "engine/combat_log.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <mutex>
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
    // CShootingSystem::UpdateShootingSystem, once a frame for each soldier.
    using UpdateFn = void(__fastcall*)(void* shooting, void* unused, float seconds);
    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x00A18410)};
    UpdateFn g_originalUpdate = nullptr;

    // CShootingSystem: the part its aim dot is on (-1 until set up), the part to set the next dot up
    // on, and whether the last shot hit.
    constexpr ptrdiff_t kDotPart = 0x1C;
    constexpr ptrdiff_t kAimPart = 0x34;
    constexpr ptrdiff_t kLastShotHit = 0x108;
    // CShootingSystem: the factors GetChanceOfSuccess multiplies, from its last call.
    constexpr ptrdiff_t kOwnStatusFactor = 0x10C;
    constexpr ptrdiff_t kTargetStatusFactor = 0x110;
    constexpr ptrdiff_t kGroupFactor = 0x114;
    constexpr ptrdiff_t kDistanceFactor = 0x11C;
    constexpr int kHeadPart = 3;
    constexpr int kTorsoPart = 5;

    // Hardcore: seconds for aim to settle after acquiring the player, at 0 m and per metre, and for a
    // soldier who has been hunting him.
    constexpr float kSettleSeconds = 0.6f;
    constexpr float kSettleSecondsPerMetre = 0.02f;
    constexpr float kHuntingSettleSeconds = 0.3f;
    // Hardcore: how long before he caught sight of the player a soldier must have been alert to settle
    // that quickly; one the player has only just alerted is still surprised.
    constexpr double kHuntingSeconds = 2.0;
    // A shooting system not updated for this long has gone, and a new soldier's may take its place;
    // more than kMostRemembered alert at once are forgotten, as surprised.
    constexpr double kGoneSeconds = 1.0;
    constexpr size_t kMostRemembered = 512;
    // Hardcore: the best chance any one shot has.
    constexpr float kMaxChance = 0.85f;
    // Hardcore: how much of the chance a shot's recoil takes from the next, and how fast it returns.
    constexpr float kRecoilLoss = 0.6f;
    constexpr float kRecoilRecoverySeconds = 0.25f;
    // Hardcore: the share of aim dots on the head rather than the torso, once his aim has held the
    // player this long; before that every dot is on the torso.
    constexpr float kHeadAimShare = 0.15f;
    constexpr float kHeadAimAfterSeconds = 3.0f;
    // Hardcore: how visible the player must be to him for a full chance.
    constexpr float kFullVisibility = 0.6f;
    // Hardcore: what each radian per second the player crosses his aim at costs the chance.
    constexpr float kCrossingLoss = 1.0f;

    // When each shooting system last fired at the player, by game clock.
    std::unordered_map<const void*, double> g_lastShot;

    // The shot at the player being rolled, not rolling between such shots.
    struct Shot {
        bool rolling = false;
        float recoil = 1.0f;
        float sighting = 1.0f;
        float crossing = 1.0f;
        float shaken = 1.0f;
        float chance = NAN;
    };
    Shot g_shot;

    // How settled his aim is, 0 to 1, from when the reaction timer started. A soldier hunting the player
    // settles quicker, and also from when he last caught sight of him, so his first shot can hit and a
    // peek from where the player vanished meets a settled rifle.
    float Settled(void* shooting, bool hunting) {
        if (hunting) {
            const float seconds = std::max(Shooting::AimSeconds(shooting), Shooting::SightSeconds(shooting));
            return std::clamp(seconds / kHuntingSettleSeconds, 0.0f, 1.0f);
        }
        const float distance = Shooting::TargetDistance(shooting);
        return std::clamp(Shooting::AimSeconds(shooting) / (kSettleSeconds + kSettleSecondsPerMetre * distance), 0.0f,
                          1.0f);
    }

    // Since when each shooting system's soldier has been alert, and when it was last updated.
    struct Alertness {
        double since;
        double updated;
    };
    std::unordered_map<const void*, Alertness> g_alertness;
    std::mutex g_alertnessLock;

    void __fastcall UpdateDetour(void* shooting, void* unused, float seconds) {
        const double now = Game::Now();
        {
            std::lock_guard<std::mutex> lock(g_alertnessLock);
            if (!Shooting::Alert(Shooting::Agent(shooting))) {
                g_alertness.erase(shooting);
            } else {
                if (g_alertness.size() > kMostRemembered) {
                    g_alertness.clear();
                }
                auto [entry, fresh] = g_alertness.try_emplace(shooting, Alertness{now, now});
                if (!fresh && now - entry->second.updated > kGoneSeconds) {
                    entry->second.since = now;
                }
                entry->second.updated = now;
            }
        }
        g_originalUpdate(shooting, unused, seconds);
    }

    // Whether he was alert for kHuntingSeconds before this sight of the player began.
    bool Hunting(void* shooting) {
        double since = 0.0;
        {
            std::lock_guard<std::mutex> lock(g_alertnessLock);
            const auto alertness = g_alertness.find(shooting);
            if (alertness == g_alertness.end()) {
                return false;
            }
            since = alertness->second.since;
        }
        const double sighted = Game::Now() - std::max(Shooting::SightSeconds(shooting), 0.0f);
        return sighted - since >= kHuntingSeconds;
    }

    float Recoil(const void* shooting) {
        const auto last = g_lastShot.find(shooting);
        if (last == g_lastShot.end()) {
            return 1.0f;
        }
        const double since = Game::Now() - last->second;
        return 1.0f - kRecoilLoss * static_cast<float>(std::exp(-since / kRecoilRecoverySeconds));
    }

    float Sighting(float visibility) { return std::min(visibility / kFullVisibility, 1.0f); }

    // How hard the player's movement across his line of fire makes the shot, as a factor.
    float Crossing(void* shooting) {
        float from[3];
        float to[3];
        if (!Shooting::Positions(shooting, from, to)) {
            return 1.0f;
        }
        void* playerPawn = Shooting::Pawn(Shooting::Target(shooting));
        float velocity[3];
        Shooting::Velocity(playerPawn, velocity);
        float line = 0.0f;
        float speed = 0.0f;
        float along = 0.0f;
        for (int i = 0; i < 3; ++i) {
            const float l = to[i] - from[i];
            const float v = velocity[i];
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

    // The game misses with every shot until his reaction timer runs, which it starts at his first shot:
    // a surprised soldier's first shot misses, a hunting one's need not.
    float __fastcall GetTimerFactorDetour(void* shooting) {
        if (Shooting::AimsAtPlayer(shooting)) {
            const bool hunting = Hunting(shooting);
            if (hunting || Shooting::Reacting(shooting)) {
                return Settled(shooting, hunting);
            }
        }
        return g_originalGetTimerFactor(shooting);
    }

    float __fastcall GetChanceDetour(void* shooting) {
        float chance = g_originalGetChance(shooting);
        if (!g_shot.rolling) {
            return chance;
        }
        // A certain hit is point blank, which the reaction timer does not reach.
        if (chance >= 1.0f) {
            chance *= Settled(shooting, Hunting(shooting));
        }
        // Evidence for tracing where the game's chance goes wrong; to go once that is fixed.
        if (std::isnan(chance)) {
            CombatLogf("soldier %08X shot at player: the game's chance is not a number: own status "
                       "%.2f, target status %.2f, group %.2f, distance %.2f",
                       Shooting::ShooterId(shooting), Read<float>(shooting, kOwnStatusFactor),
                       Read<float>(shooting, kTargetStatusFactor), Read<float>(shooting, kGroupFactor),
                       Read<float>(shooting, kDistanceFactor));
        }
        g_shot.chance = std::min(chance, kMaxChance) * g_shot.recoil * g_shot.sighting *
                        g_shot.crossing * g_shot.shaken;
        return RollChance(g_shot.chance);
    }

    int __fastcall ShootAtDetour(void* shooting, void* unused, float* position, bool* head,
                                 bool* torso) {
        if (!Shooting::AimsAtPlayer(shooting)) {
            return g_originalShootAt(shooting, unused, position, head, torso);
        }

        // No streak of forced misses after a hit.
        Write<bool>(shooting, kLastShotHit, false);
        const bool headAllowed = Shooting::AimSeconds(shooting) >= kHeadAimAfterSeconds;
        const int part = headAllowed && std::rand() < kHeadAimShare * RAND_MAX ? kHeadPart : kTorsoPart;
        if (Read<int>(shooting, kDotPart) != part) {
            Write(shooting, kAimPart, part);
            g_setupAimingDot(shooting);
        }

        const float visibility = Shooting::Visibility(shooting);
        g_shot = {true, Recoil(shooting), Sighting(visibility), Crossing(shooting),
                  Shooting::Shaken(shooting)};
        const int result = g_originalShootAt(shooting, unused, position, head, torso);
        void* player = Shooting::Pawn(Shooting::Target(shooting));
        float velocity[3];
        Shooting::Velocity(player, velocity);
        CombatLogf("soldier %08X shot at player: chance %.2f (recoil %.2f, sighting %.2f, crossing "
                   "%.2f, shaken %.2f) at %.1f m, %.1f s on him, at the %s, %s; visibility %.2f, %s; "
                   "the player %s at %.1f m/s",
                   Shooting::ShooterId(shooting), g_shot.chance, g_shot.recoil, g_shot.sighting,
                   g_shot.crossing, g_shot.shaken, Shooting::TargetDistance(shooting),
                   Shooting::AimSeconds(shooting), part == kHeadPart ? "head" : "torso",
                   Read<bool>(shooting, kLastShotHit) ? "hit" : "miss", visibility,
                   Shooting::WeaponClassName(Shooting::WeaponClass(shooting)),
                   Shooting::StanceName(Shooting::Stance(player)),
                   std::hypot(velocity[0], velocity[1], velocity[2]));
        g_shot = {};
        Remember(g_lastShot, shooting) = Game::Now();
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
    if (!Hook(g_update, &UpdateDetour, &g_originalUpdate)) {
        FCSE::Logf("hit model: the shooting system's update was not found in this build, so every soldier aims as "
                   "one surprised");
    }
}
