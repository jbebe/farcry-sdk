// The local player.
#pragma once

namespace Flashlight::Player {

// False, and logged, when this build lacks the accessor.
bool Install();

// Null outside a session.
void* Local();

}
