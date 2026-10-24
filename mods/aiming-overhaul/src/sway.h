// The hand's drift: a breathing cycle that rests calm at the bottom of each breath, and a wander,
// which move the eye a few millimetres off the gun and the aim a fraction of a degree.
#pragma once

#include "engine/aim.h"
#include "engine/look.h"

namespace AimingOverhaul::Sway {

// The eye's drift this frame, for Aim::Install; nothing while switched off.
Aim::Offset Drift(float seconds);

// Where the drift has taken the aim, for Look::Install; nothing while switched off.
Look::Angles Wander();

void SetEnabled(bool enabled);

}
