// The hit log: every bullet, blast and blade that hits the player, and every one the player lands on
// a soldier, written to fcse.log with the health it took.
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <iterator>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with one stack argument, which a free function spells __fastcall.
    using DamageFn = void(__fastcall*)(void* counters, void* unused, const uint8_t* stim);

    // CFCXCountersComponentPlayer::HandleDamageStim and CFCXCountersComponentAI::DamageHealth.
    FCSE::Relocation<DamageFn> g_playerDamage{FCSE::Uplay(0x006A2880)};
    FCSE::Relocation<DamageFn> g_soldierDamage{FCSE::Uplay(0x0069C9C0)};
    DamageFn g_originalPlayerDamage = nullptr;
    DamageFn g_originalSoldierDamage = nullptr;

    bool g_enabled = true;

    // CEntityEventStims.
    constexpr ptrdiff_t kStimInstigator = 0x40;
    constexpr ptrdiff_t kStimType = 0x48;
    constexpr ptrdiff_t kStimLevel = 0x58;
    constexpr ptrdiff_t kStimBodyParts = 0x5C;

    // CFCXCountersComponent, then the soldier's wounded state on CFCXCountersComponentAI.
    constexpr ptrdiff_t kCountersHealth = 0x44;
    constexpr ptrdiff_t kCountersHitLocations = 0x48;
    constexpr ptrdiff_t kCountersMissionManager = 0x5C;
    constexpr ptrdiff_t kSoldierWoundSeverity = 0x80;
    constexpr ptrdiff_t kSoldierWounded = 0x84;

    constexpr ptrdiff_t kCounterValue = 0x10;
    constexpr ptrdiff_t kMissionManagerAct = 0x40;
    constexpr ptrdiff_t kEntityId = 0x08;
    constexpr ptrdiff_t kEntityPosition = 0x60;

    // Stim types 3 to 5: the ones a weapon deals.
    constexpr int kFirstWeaponType = 3;
    const char* const kTypeNames[] = {"pierce", "crush", "cut"};

    // In the order CFCXCountersService::ApplyLocationMultiplier tests the bits.
    constexpr struct {
        uint32_t mask;
        const char* name;
    } kBodyParts[] = {{0x003, "torso"}, {0x040, "head"}, {0x028, "hands"},
                      {0x500, "feet"},  {0x014, "arms"}, {0x280, "legs"}};

    // The stim's type name, or null when it is not logged.
    const char* LoggedType(const uint8_t* stim) {
        const int index = Read<int>(stim, kStimType) - kFirstWeaponType;
        return g_enabled && index >= 0 && index < static_cast<int>(std::size(kTypeNames))
                   ? kTypeNames[index]
                   : nullptr;
    }

    const char* BodyPart(uint32_t parts) {
        if (parts == 0) {
            return "no body part";
        }
        for (const auto& part : kBodyParts) {
            if (parts & part.mask) {
                return part.name;
            }
        }
        return "unknown body part";
    }

    float Health(const void* counters) {
        const auto* counter = Read<const void*>(counters, kCountersHealth);
        return counter != nullptr ? Read<float>(counter, kCounterValue) : NAN;
    }

    void* Owner(void* counters) { return FCSE::ApiPointer()->EntityData->EntityOf(counters); }

    void* Instigator(const uint8_t* stim) {
        return Game::ProxyEntity(Read<const void*>(stim, kStimInstigator));
    }

    float Distance(const void* a, const void* b) {
        if (a == nullptr || b == nullptr) {
            return NAN;
        }
        const auto axis = [&](ptrdiff_t i) {
            const ptrdiff_t at = kEntityPosition + i * sizeof(float);
            return Read<float>(a, at) - Read<float>(b, at);
        };
        return std::hypot(axis(0), axis(1), axis(2));
    }

    // One line: who, what hit them where and from how far, the result, and the settings behind it.
    void LogHit(const char* who, const char* type, const uint8_t* stim, float distance,
                const void* counters, const char* result) {
        const auto* missions = Read<const void*>(counters, kCountersMissionManager);
        FCSE::Logf("%s hit: %s level %d, %s, at %.1f m, %s (%s, act %u, hit locations %s)", who,
                   type, Read<int>(stim, kStimLevel), BodyPart(Read<uint32_t>(stim, kStimBodyParts)),
                   distance, result, Game::Name(Game::CurrentDifficulty()),
                   missions != nullptr ? Read<uint8_t>(missions, kMissionManagerAct) : 0u,
                   Read<bool>(counters, kCountersHitLocations) ? "on" : "off");
    }

    void __fastcall PlayerDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const char* type = LoggedType(stim);
        if (type == nullptr) {
            g_originalPlayerDamage(counters, unused, stim);
            return;
        }

        const float before = Health(counters);
        g_originalPlayerDamage(counters, unused, stim);
        char result[64];
        std::snprintf(result, sizeof(result), "health %.0f -> %.0f", before, Health(counters));
        LogHit("player", type, stim, Distance(Owner(counters), Instigator(stim)), counters, result);
    }

    void __fastcall SoldierDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const char* type = LoggedType(stim);
        void* player = type != nullptr ? Game::PlayerEntity() : nullptr;
        if (player == nullptr || Instigator(stim) != player) {
            g_originalSoldierDamage(counters, unused, stim);
            return;
        }

        const bool wasWounded = Read<bool>(counters, kSoldierWounded);
        const float before = Health(counters);
        g_originalSoldierDamage(counters, unused, stim);
        const float after = Health(counters);

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
        void* soldier = Owner(counters);
        char who[24];
        std::snprintf(who, sizeof(who), "soldier %08X",
                      soldier != nullptr ? Read<uint32_t>(soldier, kEntityId) : 0u);
        LogHit(who, type, stim, Distance(soldier, player), counters, result);
    }
}

void InstallHitLog() {
    if (!Hook(g_playerDamage, &PlayerDamageDetour, &g_originalPlayerDamage) ||
        !Hook(g_soldierDamage, &SoldierDamageDetour, &g_originalSoldierDamage)) {
        FCSE::Logf("hit log: the player's or the soldiers' damage handling was not found in this "
                   "build");
    }
}

void __cdecl OnHitLogChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_enabled = value->asCheckbox;
}
