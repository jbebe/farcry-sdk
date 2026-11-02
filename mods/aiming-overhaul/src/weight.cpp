// What the weapon in hand weighs: `AimingOverhaul.Weight` on its archetype, which the weapon
// fragments in layer\mods\ give it (written by data\fragments.py).
#include "weight.h"

#include "fcse_api.h"

#include <algorithm>

namespace {
    // The weight of a weapon whose archetype gives none, of empty hands, and the least any weighs,
    // in kilograms.
    constexpr float kUnknownKilograms = 3.5f;
    constexpr float kEmptyKilograms = 1.0f;
    constexpr float kLightest = 0.1f;

    uint8_t* g_weapon = nullptr;
    float g_kilograms = kEmptyKilograms;
}

void AimingOverhaul::Weight::Follow(uint8_t* weapon) {
    if (weapon == g_weapon) {
        return;
    }
    g_weapon = weapon;
    const FCSE_EntityDataAPI* data = FCSE::ApiPointer()->EntityData;
    void* entity = weapon != nullptr ? data->EntityOf(weapon) : nullptr;
    g_kilograms = weapon != nullptr ? kUnknownKilograms : kEmptyKilograms;
    if (entity != nullptr) {
        data->GetFloat(entity, "AimingOverhaul.Weight", &g_kilograms);
    }
    g_kilograms = (std::max)(g_kilograms, kLightest);
}

float AimingOverhaul::Weight::Kilograms() {
    return g_kilograms;
}
