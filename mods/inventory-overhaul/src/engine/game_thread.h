// The thread the engine updates the game from.
#pragma once

namespace InventoryOverhaul::GameThread {

// False when the engine's frame was not found or could not be hooked.
bool Install();

// Whether the caller is on the game thread; false until the first frame has run.
bool IsCurrent();

}
