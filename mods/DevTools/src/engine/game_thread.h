// The engine's frame, as somewhere to run code.
//
// Anything that reaches into a live Far Cry 2 has to run on the thread driving it, at a point where
// the engine is between frames rather than halfway through one. This is that point, and the queue
// is how code that is not already there gets to it.
#pragma once

#include <functional>

namespace DevTools::GameThread {

// Hooks the engine's per-frame update. Call once from FCSE_Load. False means there is no frame to
// run on, which it logs, and everything below is inert from then on.
bool Install();

// True on the thread the engine updates from, once it has updated at least once.
bool IsCurrent();

// Runs `job` at the end of the next frame. Safe from any thread, and from inside a job - one posted
// while the queue is draining runs on the frame after.
void Post(std::function<void()> job);

}
