// Putting an entity into the world by archetype name, the way the game's own scripts do.
#pragma once

#include <string>

namespace DevTools::Spawn {

// The Lua call that spawns `archetype` at x, y, z facing `yaw` degrees, returning its entity id.
std::string Call(const char* archetype, float x, float y, float z, float yaw);

// Spawns `archetype` a few metres ahead of the player, facing the way they face, at the end of the
// next frame. Logs and does nothing outside a world.
void Ahead(const char* archetype);

// Spawns the pickup `archetype` within the player's reach, like Ahead. It starts low, because a drop
// rolls away on a slope, and unturned, because one turned like the player was not offered for use.
void Pickup(const char* archetype);

// Spawns `count` of `archetype` side by side at rifle range, facing the player, like Ahead.
void Squad(const char* archetype, int count);

}
