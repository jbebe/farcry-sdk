// The test range's scenarios, as data: where the player starts and who is spawned against him.
#pragma once

#include <span>

namespace DevTools::Scenarios {

// A place on the range's ground, facing `yaw` degrees: 0 faces north (+y), 90 west, 270 east.
struct Spot {
    float x;
    float y;
    float yaw;
};

// One soldier, and the post he holds when he should stay where he is put.
struct Soldier {
    const char* archetype;
    Spot at;
    const char* post = nullptr;
};

struct Scenario {
    const char* name;
    // The Difficulty Overhaul finish checklist rows it covers.
    const char* rows;
    std::span<const Soldier> soldiers;
    const char* steps;
    const char* expect;
};

// Scenarios that share one stretch of the range and the player's start on it.
struct Arena {
    const char* name;
    Spot start;
    std::span<const Scenario> scenarios;
};

std::span<const Arena> Arenas();

}
