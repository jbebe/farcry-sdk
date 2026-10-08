#pragma once

#include "api/entity_data_store.h"

#include <mutex>

// CFCSEDataComponent: FCSE's own entity component, which carries an EntityDataStore on any entity.
// The engine creates it from an archetype's data, saves it with the entity and recreates it on
// load, through the same factory and property registry as its own components - see
// docs/docs/engine-internals/fcse-entity-data-abi.md.
namespace FCSE {

class EntityDataComponent {
public:
    // Registers the class with the engine's component factory. Call once, after InitDuniaEngine
    // and before a world loads; a failure is logged and leaves every store lookup returning null.
    static bool Install();

    // Held around every StoreOf and every use of what it returns: the engine reads and writes the
    // stores from its own threads when it loads and saves.
    static std::unique_lock<std::recursive_mutex> Lock();

    // The entity's store, or null. With `create`, an entity without the component is given one.
    static EntityDataStore* StoreOf(void* entity, bool create);

    // Gives the entity a CPersistComponent at the level the engine uses for entities it carries
    // between worlds, so that it is saved with the game.
    static bool Persist(void* entity);

    // The entity a component belongs to, or null.
    static void* EntityOf(void* component);
};

}
