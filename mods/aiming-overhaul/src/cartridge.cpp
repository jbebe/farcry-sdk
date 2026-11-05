// The round the weapon in hand fires. See cartridge.h.
#include "cartridge.h"

#include "fcse_api.h"

#include <atomic>

namespace {
    uint8_t* g_weapon = nullptr;
    std::atomic<float> g_impulse{0.0f};
    char g_name[32] = {};
}

void AimingOverhaul::Cartridge::Follow(uint8_t* weapon) {
    if (weapon == g_weapon) {
        return;
    }
    g_weapon = weapon;
    g_name[0] = '\0';
    g_impulse = 0.0f;
    const FCSE_EntityDataAPI* data = FCSE::ApiPointer()->EntityData;
    void* entity = weapon != nullptr ? data->EntityOf(weapon) : nullptr;
    float grams = 0.0f;
    float velocity = 0.0f;
    int32_t projectiles = 1;
    if (entity == nullptr || !data->GetFloat(entity, "Cartridge.BulletMass", &grams) ||
        !data->GetFloat(entity, "Cartridge.MuzzleVelocity", &velocity)) {
        return;
    }
    data->GetInt(entity, "Cartridge.Projectiles", &projectiles);
    data->GetString(entity, "Cartridge.Name", g_name, sizeof(g_name));
    g_impulse = static_cast<float>(projectiles) * grams / 1000.0f * velocity;
}

float AimingOverhaul::Cartridge::Impulse() {
    return g_impulse;
}

const char* AimingOverhaul::Cartridge::Name() {
    return g_name;
}
