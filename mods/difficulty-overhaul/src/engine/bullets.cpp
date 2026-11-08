// A gun's CWeaponFireBulletStrategy and its bullets.
#include "engine/bullets.h"

#include "engine/memory.h"

namespace {
    // CWeaponFireBulletStrategy's CWeaponFireBulletProperties, and the properties' impact record.
    constexpr ptrdiff_t kStrategyProperties = 0x50;
    constexpr ptrdiff_t kPropertiesImpactRecord = 0x4FC;

    // A fired bullet's queue entry.
    constexpr ptrdiff_t kBulletDirection = 0x1C;
}

namespace DifficultyOverhaul::Bullets {

FCSE::Relocation<SpawnPuffFn> SpawnPuff{FCSE::Uplay(0x0010F3B0)};
FCSE::Relocation<ApplyBulletFn> ApplyBullet{FCSE::Uplay(0x00115680)};

const void* ImpactRecord(const uint8_t* strategy) {
    const auto* properties = Read<const uint8_t*>(strategy, kStrategyProperties);
    return properties != nullptr ? Read<const void*>(properties, kPropertiesImpactRecord) : nullptr;
}

const float* Direction(const uint8_t* bullet) { return Vec3(bullet, kBulletDirection); }

}
