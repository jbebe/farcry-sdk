// The end of the engine's frame, as somewhere to run code once a frame on the game thread.
#pragma once

namespace Flashlight::Frame {

using TickFn = void (*)(float seconds);

// Calls `tick` at the end of every CXGame::Update with the seconds since the last one. False, and
// logged, when this build has no such site.
bool Install(TickFn tick);

}
