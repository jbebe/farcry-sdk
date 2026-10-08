// The entity-data store's layering, and the hash its keys and class id are stored under.

#include <string>

#include <gtest/gtest.h>

#include "api/entity_data_store.h"
#include "util/crc32.h"

namespace {

using FCSE::Crc32;
using FCSE::EntityDataStore;

// Ids read out of Dunia.dll, so a CRC that drifts from the engine's fails here and not in a save.
TEST(Crc32, MatchesTheEnginesNameHashes) {
    EXPECT_EQ(Crc32("CGraphicComponent"), 0x035982C6u);
    EXPECT_EQ(Crc32("hidComponentClassName"), 0x431EBA8Eu);
    EXPECT_EQ(Crc32("hidHasAliasName"), 0xFEE21F0Du);
}

TEST(EntityDataStore, StateOverridesAuthored) {
    EntityDataStore store;
    store.authored[1] = 10;
    store.state[1] = 20;

    ASSERT_NE(store.Find(1), nullptr);
    EXPECT_EQ(std::get<int32_t>(*store.Find(1)), 20);
}

TEST(EntityDataStore, AuthoredShowsWhenNothingWasSet) {
    EntityDataStore store;
    store.authored[1] = std::string("data");

    ASSERT_NE(store.Find(1), nullptr);
    EXPECT_EQ(std::get<std::string>(*store.Find(1)), "data");
    EXPECT_EQ(store.Find(2), nullptr);
}

TEST(EntityDataStore, MergedKeepsAuthoredKeysAndTakesStateValues) {
    EntityDataStore store;
    store.authored[1] = 1.5f;
    store.authored[2] = 7;
    store.state[2] = 8;
    store.state[3] = std::string("set");

    const EntityDataStore::Layer merged = store.Merged();
    ASSERT_EQ(merged.size(), 3u);
    EXPECT_EQ(std::get<float>(merged.at(1)), 1.5f);
    EXPECT_EQ(std::get<int32_t>(merged.at(2)), 8);
    EXPECT_EQ(std::get<std::string>(merged.at(3)), "set");
}

}
