#include "remote/hit_log.h"

#include "engine/damage.h"
#include "engine/difficulty.h"
#include "engine/entity.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <cstdio>

namespace {
    using namespace DevTools;

    Damage::StimHandlerFn g_originalPlayerDamage = nullptr;
    Damage::StimHandlerFn g_originalSoldierDamage = nullptr;

    // CFCXCountersComponent, then the soldier's wounded state on CFCXCountersComponentAI.
    constexpr ptrdiff_t kCountersHitLocations = 0x48;
    constexpr ptrdiff_t kCountersMissionManager = 0x5C;
    constexpr ptrdiff_t kSoldierWoundSeverity = 0x80;
    constexpr ptrdiff_t kSoldierWounded = 0x84;

    constexpr ptrdiff_t kMissionManagerAct = 0x40;

    const char* const kTypeNames[] = {"pierce", "crush", "cut"};
    const char* const kPartNames[] = {"head", "torso", "arms", "legs", "hands", "feet", "no body part"};

    // The stim's type name, or null for a type that is not logged.
    const char* LoggedType(const uint8_t* stim) {
        const int type = Damage::Type(stim);
        return type >= Damage::kPierce && type <= Damage::kCut ? kTypeNames[type - Damage::kPierce]
                                                                : nullptr;
    }

    Entity::Health HealthOf(const void* counters) {
        return Entity::CountersHealth(counters).value_or(Entity::Health{NAN, NAN});
    }

    float Distance(void* a, void* b) {
        if (a == nullptr || b == nullptr) {
            return NAN;
        }
        const float* p = Entity::Matrix(a) + 12;
        const float* q = Entity::Matrix(b) + 12;
        return std::hypot(p[0] - q[0], p[1] - q[1], p[2] - q[2]);
    }

    void* EntityOf(void* counters) { return FCSE::ApiPointer()->EntityData->EntityOf(counters); }

    // One line: who, what hit them where and from how far, the result, and the settings behind it.
    void SendHit(const char* who, const char* type, const uint8_t* stim, float distance,
                 const void* counters, const char* result) {
        const auto* missions = Read<const void*>(counters, kCountersMissionManager);
        FCSE::Sendf("combat", "%s hit: %s level %d, %s, at %.1f m, %s (%s, act %u, hit locations %s)",
                    who, type, Damage::Level(stim), kPartNames[Damage::Part(stim)], distance, result,
                    Difficulty::Name(),
                    missions != nullptr ? Read<uint8_t>(missions, kMissionManagerAct) : 0u,
                    Read<bool>(counters, kCountersHitLocations) ? "on" : "off");
    }

    void __fastcall PlayerDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const char* type = LoggedType(stim);
        if (type == nullptr) {
            g_originalPlayerDamage(counters, unused, stim);
            return;
        }

        const float before = HealthOf(counters).value;
        g_originalPlayerDamage(counters, unused, stim);
        const Entity::Health after = HealthOf(counters);
        void* shooter = Damage::Instigator(stim);
        char result[96];
        std::snprintf(result, sizeof(result), "health %.0f -> %.0f of %.0f, by %08X", before,
                      after.value, after.max, Entity::Id(shooter));
        const float distance = Distance(EntityOf(counters), shooter);
        SendHit("player", type, stim, distance, counters, result);
        if (before > 0.0f && after.value <= 0.0f) {
            FCSE::Sendf("combat", "player died: killed by %08X at %.1f m", Entity::Id(shooter),
                        distance);
        }
    }

    void __fastcall SoldierDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const char* type = LoggedType(stim);
        if (type == nullptr || !Damage::ByPlayer(stim)) {
            const float before = HealthOf(counters).value;
            g_originalSoldierDamage(counters, unused, stim);
            if (before > 0.0f && HealthOf(counters).value <= 0.0f) {
                FCSE::Sendf("combat", "soldier %08X died, not by the player: stim type %d level %d",
                            Entity::Id(EntityOf(counters)), Damage::Type(stim), Damage::Level(stim));
            }
            return;
        }

        const bool wasWounded = Read<bool>(counters, kSoldierWounded);
        const float before = HealthOf(counters).value;
        g_originalSoldierDamage(counters, unused, stim);
        const float after = HealthOf(counters).value;

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
        void* soldier = EntityOf(counters);
        char who[24];
        std::snprintf(who, sizeof(who), "soldier %08X", Entity::Id(soldier));
        SendHit(who, type, stim, Distance(soldier, Damage::Instigator(stim)), counters, result);
    }

    bool Hook(const FCSE::Relocation<Damage::StimHandlerFn>& site, Damage::StimHandlerFn detour,
              Damage::StimHandlerFn* original) {
        return site && FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                                reinterpret_cast<void*>(detour),
                                                reinterpret_cast<void**>(original));
    }
}

namespace DevTools::HitLog {

void Install() {
    if (!Hook(Damage::PlayerDamage, &PlayerDamageDetour, &g_originalPlayerDamage) ||
        !Hook(Damage::SoldierDamage, &SoldierDamageDetour, &g_originalSoldierDamage)) {
        FCSE::ApiPointer()->Log("combat log: the player's or the soldiers' damage handling was not "
                                "found in this build - no hits are logged");
    }
}

}
