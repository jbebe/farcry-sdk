// What the Spawner tab can put in the world, as data: an entity-library archetype per row.
#pragma once

#include <span>

namespace DevTools::Spawnables {

// `archetype` is the fully qualified hidName the entity library is keyed by.
struct Spawnable {
    const char* label;
    const char* archetype;
};

struct Group {
    const char* name;
    std::span<const Spawnable> spawnables;
};

std::span<const Group> All();

}
