// Damage dealt: the player's hits on a soldier count by body part with the overhaul's multipliers in
// place of the game's.
#include "engine/damage.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>
#include <iterator>

using namespace DifficultyOverhaul;

namespace {
    Damage::StimHandlerFn g_originalSoldierDamage = nullptr;

    // Hardcore multiplier per Damage::BodyPart for the player's hits on a soldier.
    constexpr float kPartMultipliers[] = {6.0f, 2.5f, 1.25f, 1.25f, 0.75f, 0.75f};
    constexpr size_t kParts = std::size(kPartMultipliers);
    static_assert(kParts == Damage::kNoBodyPart);

    void __fastcall SoldierDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        float* multipliers = Damage::ByPlayer(stim) ? Damage::PartMultipliers(counters) : nullptr;
        if (multipliers == nullptr) {
            g_originalSoldierDamage(counters, unused, stim);
            return;
        }

        float stock[kParts];
        std::copy_n(multipliers, kParts, stock);
        std::copy_n(kPartMultipliers, kParts, multipliers);
        g_originalSoldierDamage(counters, unused, stim);
        std::copy_n(stock, kParts, multipliers);
    }
}

void InstallDamageDealt() {
    if (!Hook(Damage::SoldierDamage, &SoldierDamageDetour, &g_originalSoldierDamage)) {
        FCSE::Logf("damage dealt: the soldiers' damage handling was not found in this build");
    }
}
