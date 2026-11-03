// The game clock, which stopped stands the world still while frames keep coming.
#pragma once

namespace DevTools::Clock {

// Whether this build's clock was found; without it the clock never stops.
bool Found();

// Stops game time, or starts it again at its normal pace.
void SetStopped(bool stopped);

bool Stopped();

// The last frame's real time, clamped as the engine clamps it, whether or not the clock is stopped.
float RealDelta();

}
