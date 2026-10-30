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
#include <optional>
#include <string>

namespace {
    // Far enough ahead to clear the longest vehicle, the big truck.
    constexpr float kAhead = 8.0f;

    // A squad stands at rifle range, a step apart.
    constexpr float kSquadAhead = 25.0f;
    constexpr float kSquadSpacing = 3.0f;

    // The spawn does not find the ground, so the entity starts above the player's feet and drops.
    constexpr float kAbove = 1.5f;

    constexpr float kPickupAhead = 1.6f;
    constexpr float kPickupAbove = 0.4f;

    constexpr float kDegreesPerRadian = 57.2957795f;
}

namespace DevTools::Spawn {

namespace {
    // Spawns `count` side by side `ahead` metres in front of the player and `above` over their feet,
    // turned `turnDegrees` from them, or unturned without it.
    void Line(const char* archetype, int count, float ahead, float above, std::optional<float> turnDegrees) {
        GameThread::Post([name = std::string(archetype), count, ahead, above, turnDegrees] {
            void* ref = Entity::AcquireFocusRef(Player::CameraManager());
            void* entity = Entity::Of(ref);
            if (entity == nullptr) {
                Entity::ReleaseRef(ref);
                FCSE::Logf("spawn: no player to spawn %s in front of", name.c_str());
                return;
            }

            const float* matrix = Entity::Matrix(entity);
            const float yaw = Entity::Yaw(entity);
            const float x = matrix[12] - std::sin(yaw) * ahead;
            const float y = matrix[13] + std::cos(yaw) * ahead;
            const float z = matrix[14] + above;
            Entity::ReleaseRef(ref);

            for (int i = 0; i < count; ++i) {
                const float across = (i - (count - 1) * 0.5f) * kSquadSpacing;
                Lua::Run(Call(name.c_str(), x + std::cos(yaw) * across, y + std::sin(yaw) * across, z,
                              turnDegrees ? yaw * kDegreesPerRadian + *turnDegrees : 0.0f));
            }
        });
    }
}

std::string Call(const char* archetype, float x, float y, float z, float yaw) {
    char call[256];
    std::snprintf(call, sizeof(call), "SpawnEntityFromArchetype(\"%s\", %.3f, %.3f, %.3f, 0, 0, %.1f)",
                  archetype, x, y, z, yaw);
    return call;
}

void Ahead(const char* archetype) { Line(archetype, 1, kAhead, kAbove, 0.0f); }

void Squad(const char* archetype, int count) { Line(archetype, count, kSquadAhead, kAbove, 180.0f); }

void Pickup(const char* archetype) { Line(archetype, 1, kPickupAhead, kPickupAbove, std::nullopt); }

}
