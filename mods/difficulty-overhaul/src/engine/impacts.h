// The game's material impact records, and decals spawned from them where a ray meets the world.
#pragma once

#include "engine/ray.h"

#include <cstdint>

namespace DifficultyOverhaul::Impacts {

// False, and logged, when the records or their decals are not found.
bool Install();

// The CMaterialImpactFx named by the CRC-32 of its name in materialimpacts.xml, or null.
const void* Record(uint32_t name);

// Spawns the record's decal for the surface a ray hit, where a bullet hole would go: on the terrain, on the
// object hit or on the static geometry hit. An object shows the part of it that falls on the object, where
// the game would place its own decals only whole. The game thread only.
void SpawnDecal(const void* record, const Ray::Hit& hit);

}
