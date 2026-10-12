// Putting an entity into the world by archetype name, the way the game's own scripts do.
#pragma once

namespace DevTools::Spawn {

// Spawns `archetype` a few metres ahead of the player, facing the way they face, at the end of the
// next frame. Logs and does nothing outside a world.
void Ahead(const char* archetype);

}
