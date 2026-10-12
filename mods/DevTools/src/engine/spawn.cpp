// Putting an entity into the world by archetype name, through the SpawnEntityFromArchetype Lua
// global. What it does with its arguments is in docs/docs/engine-internals/lua-api-surface.md.
#include "engine/spawn.h"

#include "engine/entity.h"
#include "engine/game_thread.h"
#include "engine/lua.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdio>
#include <string>

namespace {
    // Far enough ahead to clear the longest vehicle, the big truck.
    constexpr float kAhead = 8.0f;

    // The spawn does not find the ground, so the entity starts above the player's feet and drops.
    constexpr float kAbove = 1.5f;

    constexpr float kDegreesPerRadian = 57.2957795f;
}

namespace DevTools::Spawn {

void Ahead(const char* archetype) {
    GameThread::Post([name = std::string(archetype)] {
        void* ref = Entity::AcquireFocusRef(Player::CameraManager());
        void* entity = Entity::Of(ref);
        if (entity == nullptr) {
            Entity::ReleaseRef(ref);
            FCSE::Logf("spawn: no player to spawn %s in front of", name.c_str());
            return;
        }

        const float* matrix = Entity::Matrix(entity);
        const float yaw = Entity::Yaw(entity);

        char line[256];
        std::snprintf(line, sizeof(line),
                      "SpawnEntityFromArchetype(\"%s\", %.3f, %.3f, %.3f, 0, 0, %.1f)", name.c_str(),
                      matrix[12] - std::sin(yaw) * kAhead, matrix[13] + std::cos(yaw) * kAhead,
                      matrix[14] + kAbove, yaw * kDegreesPerRadian);
        Entity::ReleaseRef(ref);

        Lua::Run(line);
    });
}

}
