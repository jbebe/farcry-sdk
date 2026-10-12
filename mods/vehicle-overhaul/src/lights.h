// Car lights: every lit headlamp seen as a bright point, and the headlight lighting the grass.
#pragma once

namespace VehicleOverhaul::Lights {

// False, and logged, when this build lacks what the lights need; nothing of them is drawn then.
bool Install();

}
