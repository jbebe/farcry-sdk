// The thread the engine updates the game from.
#pragma once

namespace InventoryOverhaul::GameThread {

// False when the engine's frame was not found or could not be hooked. Hooks it once however often
// it is called.
bool Install();

// Whether the caller is on the game thread; false until the first frame has run.
bool IsCurrent();

// Runs `task` at the end of every frame, on the game thread.
void EachFrame(void (*task)());

}
