// Blood behind a hit: a round over 1,000 J that hits a body leaves a spatter on the ground or a wall up to
// 3 m on along its line.
#include "engine/bullets.h"
#include "engine/impacts.h"
#include "engine/memory.h"
#include "engine/ray.h"
#include "fcse_api.h"

#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    Bullets::ApplyBulletFn g_originalApplyBullet = nullptr;
    Bullets::SpawnPuffFn g_originalSpawnPuff = nullptr;

    // The records data\weapons.py gives every round over 1,000 J, and the spatter's.
    constexpr uint32_t kHeadBlood = FCSE::Crc32("Weapon.HeadBlood");
    constexpr uint32_t kAllBlood = FCSE::Crc32("Weapon.Blood");
    constexpr uint32_t kSpatter = FCSE::Crc32("Blood.Spatter");
    constexpr float kReach = 3.0f;

    // The direction of the bullet whose hits spawn now.
    thread_local const float* t_direction = nullptr;

    void __fastcall ApplyBulletDetour(uint8_t* strategy, void* unused, const uint8_t* bullet) {
        t_direction = Bullets::Direction(bullet);
        g_originalApplyBullet(strategy, unused, bullet);
        t_direction = nullptr;
    }

    void __fastcall SpawnPuffDetour(uint8_t* strategy, void* unused, void* shooter, const Ray::Hit* hit,
                                    char flag) {
        g_originalSpawnPuff(strategy, unused, shooter, hit, flag);
        if (t_direction == nullptr || !Ray::OnBody(*hit)) {
            return;
        }
        const void* record = Bullets::ImpactRecord(strategy);
        Ray::Hit surface;
        if (record != nullptr && (record == Impacts::Record(kHeadBlood) || record == Impacts::Record(kAllBlood)) &&
            Ray::FirstSurface(hit->point, t_direction, kReach, surface)) {
            Impacts::SpawnDecal(Impacts::Record(kSpatter), surface);
        }
    }
}

void InstallBloodSpatter() {
    if (!Impacts::Install() || !Hook(Bullets::ApplyBullet, &ApplyBulletDetour, &g_originalApplyBullet) ||
        !Hook(Bullets::SpawnPuff, &SpawnPuffDetour, &g_originalSpawnPuff)) {
        FCSE::Logf("blood spatter: the bullets' hits were not found in this build, so nothing spatters behind");
    }
}
