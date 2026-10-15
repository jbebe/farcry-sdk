// The game's signal dispatcher: every action-map signal the player's bindings send.
#pragma once

#include <cstdint>

namespace Flashlight::Input {

// Called with each signal's id, the CRC32 of its name in the action map.
using SignalFn = void (*)(uint32_t signal);

// Logged when the dispatcher can't be hooked.
void Install(SignalFn onSignal);

}
