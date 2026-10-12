// The big truck's radiator fan, spun by a Havok motor while the player's engine runs.
#pragma once

namespace VehicleOverhaul::Fan {

// False, and logged, when this build lacks what the fan needs; it then stays a still part.
bool Install();

}
