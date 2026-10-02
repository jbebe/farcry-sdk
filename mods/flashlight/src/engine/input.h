// The game's signal dispatcher: every action-map signal the player's bindings send.
#pragma once

#include <cstdint>

namespace Flashlight::Input {

// Called with each signal's id, the CRC32 of its name in the action map.
using SignalFn = void (*)(uint32_t signal);

// False, and logged, when this build has no dispatcher to watch.
bool Install(SignalFn onSignal);

}
