// The player's sprint press, which can be refused while down the sights.
#pragma once

namespace AimingOverhaul::Sprint {

// Whether a sprint press while down the sights or a scope is refused now.
using RefuseFn = bool (*)();

// False, and logged, when the press cannot be hooked.
bool Install(RefuseFn refuse);

}
