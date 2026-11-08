// A gun's CWeaponFireBulletStrategy and its bullets.
#pragma once

#include "engine/ray.h"
#include "fcse_api.h"

#include <cstdint>

namespace DifficultyOverhaul::Bullets {

// __thiscall with three stack arguments and one, which a free function spells __fastcall.
using SpawnPuffFn = void(__fastcall*)(uint8_t* strategy, void* unused, void* shooter, const Ray::Hit* hit,
                                      char flag);
using ApplyBulletFn = void(__fastcall*)(uint8_t* strategy, void* unused, const uint8_t* bullet);

// CWeaponFireBulletStrategy::SpawnBulletPuff, for one hit of a bullet.
extern FCSE::Relocation<SpawnPuffFn> SpawnPuff;

// CWeaponFireBulletStrategy::ApplyDelayBullet, for one fired bullet once its ray is cast; it spawns
// each of the bullet's hits.
extern FCSE::Relocation<ApplyBulletFn> ApplyBullet;

// The CMaterialImpactFx the gun's bullets play where they hit, or null.
const void* ImpactRecord(const uint8_t* strategy);

// The unit vector a fired bullet flies along.
const float* Direction(const uint8_t* bullet);

}
