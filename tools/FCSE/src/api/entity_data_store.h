#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <variant>

namespace FCSE {

using EntityValue = std::variant<int32_t, float, std::string>;

// One entity's values, keyed by the CRC-32 of their names, in two layers: what the entity's data
// authored, and what plugins set while it lived. A read prefers the second; only the second is
// saved with the game, so a later edit to the data still reaches an entity in an old save.
struct EntityDataStore {
    using Layer = std::map<uint32_t, EntityValue>;

    Layer authored;
    Layer state;

    const EntityValue* Find(uint32_t key) const {
        auto it = state.find(key);
        if (it != state.end()) {
            return &it->second;
        }
        it = authored.find(key);
        return it != authored.end() ? &it->second : nullptr;
    }

    // Every value as a read would see it, for writing the entity back out as data.
    Layer Merged() const {
        Layer merged = authored;
        for (const auto& [key, value] : state) {
            merged.insert_or_assign(key, value);
        }
        return merged;
    }
};

}
