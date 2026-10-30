// The world as something that loads: whether it has, and moving the player across it.
#pragma once

#include <optional>

namespace DevTools::World {

// Resolves the calls below. Call once from FCSE_Load. A site not found is logged.
void Install();

// Whether a loading screen is up, read off the render thread's mode. Game thread.
bool LoadingScreen();

// Whether the world still has requests to stream in. Game thread.
bool Streaming();

// Moves the local player to x, y, z, facing `yawDegrees` if given, and returns once the world there
// has loaded. Game thread. False when there is no player, or this build has no such call.
bool Teleport(float x, float y, float z, std::optional<float> yawDegrees);

}
