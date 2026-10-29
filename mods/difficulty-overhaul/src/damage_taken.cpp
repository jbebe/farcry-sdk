// Damage taken: a bullet hits the player harder by where it lands, and the last health bar no longer
// quarters the damage.
#include "engine/damage.h"
#include "engine/game.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <iterator>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with three stack arguments; `receiver` is a TEntityHandle the callee releases.
    using ApplyLocationMultiplierFn = float(__fastcall*)(void* service, void* unused, void* receiver,
                                                         const uint8_t* stim, float damage);

    // CFCXCountersService::ApplyLocationMultiplier.
    FCSE::Relocation<ApplyLocationMultiplierFn> g_applyLocationMultiplier{FCSE::Uplay(0x007645E0)};
    ApplyLocationMultiplierFn g_originalApplyLocationMultiplier = nullptr;

    // CFCXCountersComponentPlayerSP::ApplyDamageMultiplier, which quarters the damage while health
    // is in the last bar; patched to return at once.
    FCSE::Relocation<uint8_t*> g_lastBarMultiplier{
        FCSE::Pattern("E8 ?? ?? ?? ?? 84 C0 74 14 8B 44 24 04 F3 0F 10 00 F3 0F 59 05 ?? ?? ?? ?? "
                      "F3 0F 11 00 C2 04 00")};
    constexpr uint8_t kReturn4[] = {0xC2, 0x04, 0x00};

    // Hardcore: a bullet's damage to the player as a multiple of the game's, per Damage::BodyPart.
    constexpr float kBulletDamage[] = {24.0f, 5.0f, 3.0f, 3.0f, 2.0f, 2.0f, 5.0f};
    static_assert(std::size(kBulletDamage) == Damage::kNoBodyPart + 1);

    float __fastcall ApplyLocationMultiplierDetour(void* service, void* unused, void* receiver,
                                                   const uint8_t* stim, float damage) {
        const bool onPlayer = Damage::Type(stim) == Damage::kPierce &&
                              Game::IsPlayerEntity(Game::ProxyEntity(receiver));
        const float result =
            g_originalApplyLocationMultiplier(service, unused, receiver, stim, damage);
        return onPlayer ? damage * kBulletDamage[Damage::Part(stim)] : result;
    }
}

void InstallDamageTaken() {
    if (!g_lastBarMultiplier ||
        !FCSE::ApiPointer()->Patch(g_lastBarMultiplier, kReturn4, sizeof(kReturn4))) {
        FCSE::Logf("damage taken: the last health bar's damage multiplier was not found in this "
                   "build");
    }
    if (!Hook(g_applyLocationMultiplier, &ApplyLocationMultiplierDetour,
              &g_originalApplyLocationMultiplier)) {
        FCSE::Logf("damage taken: the hit location multiplier was not found in this build");
    }
}
