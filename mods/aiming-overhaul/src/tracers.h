// The player's own tracers, a streak's length against its shot, ricochets, and how thin a streak is
// drawn. Which guns fire tracers and how fast they fly is the layer's weapon data.
#pragma once

namespace AimingOverhaul::Tracers {

// Hooks where a shot decides on its tracer. False, and logged, when that place is not found.
bool Install();

// Whether the player's tracers, the streak's cap, the ricochets and the least width apply.
void SetEnabled(bool enabled);

}
