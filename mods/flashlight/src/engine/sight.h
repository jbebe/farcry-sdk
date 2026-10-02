// The guards' sight of the player while the flashlight is on.
#pragma once

#include "engine/light.h"

namespace Flashlight::Sight {

// False, and logged, when this build lacks the guards' vision setup.
bool Install();

// The lit beam, where `pose` puts it and as long and wide as `spot`. Call it every frame the light
// is on.
void SetBeam(const Light::Pose& pose, const Light::Spot& spot);

// The light is off, or gone with its world.
void Clear();

}
