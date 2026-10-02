// The guards' sight of the player while the flashlight is on.
#pragma once

namespace Flashlight::Sight {

// False, and logged, when this build lacks the guards' vision setup. The light works without it.
bool Install();

// The lit beam: from `origin` along the unit `direction`, reaching `range` metres in a cone
// `outerAngle` radians wide. Call it every frame the light is on.
void SetBeam(const float* origin, const float* direction, float range, float outerAngle);

// The light is off, or gone with its world.
void Clear();

}
