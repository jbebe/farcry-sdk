// The hit log: every bullet, blast and blade that hits the player, and every one the player lands on
// a soldier, written to fcse.log with the health it took.
#include "engine/damage.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <cstdio>

using namespace DifficultyOverhaul;

namespace {
    Damage::StimHandlerFn g_originalPlayerDamage = nullptr;
    Damage::StimHandlerFn g_originalSoldierDamage = nullptr;

    // CFCXCountersComponent, then the soldier's wounded state on CFCXCountersComponentAI.
    constexpr ptrdiff_t kCountersHitLocations = 0x48;
    constexpr ptrdiff_t kCountersMissionManager = 0x5C;
    constexpr ptrdiff_t kSoldierWoundSeverity = 0x80;
    constexpr ptrdiff_t kSoldierWounded = 0x84;

    constexpr ptrdiff_t kMissionManagerAct = 0x40;
    constexpr ptrdiff_t kEntityId = 0x08;

    const char* const kTypeNames[] = {"pierce", "crush", "cut"};
    const char* const kPartNames[] = {"head", "torso", "arms", "legs", "hands", "feet", "no body part"};

    // The stim's type name, or null for a type that is not logged.
    const char* LoggedType(const uint8_t* stim) {
        const int type = Damage::Type(stim);
        return type >= Damage::kPierce && type <= Damage::kCut ? kTypeNames[type - Damage::kPierce]
                                                                : nullptr;
    }

    float Distance(const void* a, const void* b) {
        if (a == nullptr || b == nullptr) {
            return NAN;
        }
        float p[3];
        float q[3];
        Game::Position(a, p);
        Game::Position(b, q);
        return Game::Distance(p, q);
    }

    // One line: who, what hit them where and from how far, the result, and the settings behind it.
    void LogHit(const char* who, const char* type, const uint8_t* stim, float distance,
                const void* counters, const char* result) {
        const auto* missions = Read<const void*>(counters, kCountersMissionManager);
        FCSE::Logf("%s hit: %s level %d, %s, at %.1f m, %s (%s, act %u, hit locations %s)", who,
                   type, Damage::Level(stim), kPartNames[Damage::Part(stim)], distance, result,
                   Game::Name(Game::CurrentDifficulty()),
                   missions != nullptr ? Read<uint8_t>(missions, kMissionManagerAct) : 0u,
                   Read<bool>(counters, kCountersHitLocations) ? "on" : "off");
    }

    void __fastcall PlayerDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const char* type = LoggedType(stim);
        if (type == nullptr) {
            g_originalPlayerDamage(counters, unused, stim);
            return;
        }

        const float before = Damage::Health(counters);
        g_originalPlayerDamage(counters, unused, stim);
        char result[64];
        std::snprintf(result, sizeof(result), "health %.0f -> %.0f", before,
                      Damage::Health(counters));
        LogHit("player", type, stim, Distance(Game::EntityOf(counters), Damage::Instigator(stim)),
               counters, result);
    }

    void __fastcall SoldierDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const char* type = LoggedType(stim);
        if (type == nullptr || !Damage::ByPlayer(stim)) {
            g_originalSoldierDamage(counters, unused, stim);
            return;
        }

        const bool wasWounded = Read<bool>(counters, kSoldierWounded);
        const float before = Damage::Health(counters);
        g_originalSoldierDamage(counters, unused, stim);
        const float after = Damage::Health(counters);

        char result[64];
        if (wasWounded) {
            std::snprintf(result, sizeof(result), "wound severity %d",
                          Read<int>(counters, kSoldierWoundSeverity));
        } else {
            std::snprintf(result, sizeof(result), "health %.0f -> %.0f%s", before, after,
                          after <= 0.0f                          ? ", killed"
                          : Read<bool>(counters, kSoldierWounded) ? ", wounded"
                                                                   : "");
        }
        void* soldier = Game::EntityOf(counters);
        char who[24];
        std::snprintf(who, sizeof(who), "soldier %08X",
                      soldier != nullptr ? Read<uint32_t>(soldier, kEntityId) : 0u);
        LogHit(who, type, stim, Distance(soldier, Damage::Instigator(stim)), counters, result);
    }
}

void InstallHitLog() {
    if (!Hook(Damage::PlayerDamage, &PlayerDamageDetour, &g_originalPlayerDamage) ||
        !Hook(Damage::SoldierDamage, &SoldierDamageDetour, &g_originalSoldierDamage)) {
        FCSE::Logf("hit log: the player's or the soldiers' damage handling was not found in this "
                   "build");
    }
}
