// The game's signal dispatcher: every action-map signal the player's bindings send.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Input {

// Called with each signal's id, the CRC32 of its name in the action map. A signal bound in more
// than one active action map arrives once from each.
using SignalFn = void (*)(uint32_t signal);

// False, and logged, when the dispatcher cannot be hooked.
bool Install(SignalFn onSignal);

}
