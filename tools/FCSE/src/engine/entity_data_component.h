#pragma once

#include "engine/entity_data_store.h"

#include <cstdint>
#include <optional>

// CFCSEDataComponent: FCSE's own entity component, carrying an EntityDataStore on any entity. See
// docs/docs/engine-internals/fcse-entity-data-abi.md.
namespace FCSE {

class EntityDataComponent {
public:
    // Registers the class with the engine's component factory; logs why and returns false if not.
    static bool Install();

    // A value on the entity, by the CRC-32 of its key. Write gives the entity the component if it
    // has none; Erase drops a value set at runtime.
    static std::optional<EntityValue> Read(void* entity, uint32_t key);
    static bool Write(void* entity, uint32_t key, EntityValue value);
    static bool Erase(void* entity, uint32_t key);

    // Gives the entity a CPersistComponent at the level the engine keeps world-travelling entities at.
    static bool Persist(void* entity);

    // The entity a component belongs to, or null.
    static void* EntityOf(void* component);
};

}
