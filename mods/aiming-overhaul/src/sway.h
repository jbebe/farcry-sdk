// The hand's drift: a breathing cycle that rests calm at the bottom of each breath, a wander, and a
// jolt with each beat of the heart, which move the eye a few millimetres off the gun and the aim a
// fraction of a degree, all of it quicker and wider as the heart races.
#pragma once

#include "engine/aim.h"
#include "engine/look.h"

namespace AimingOverhaul::Sway {

// The eye's drift this frame, for Aim::Install; nothing while switched off.
Aim::Offset Drift(float seconds);

// Where the drift has taken the aim, for Look::Install; nothing while switched off.
Look::Angles Wander();

// Whether the breath began drawing in or letting out in the frame Drift last moved.
enum class Turn { None, In, Out };
Turn Turned();

void SetEnabled(bool enabled);

}
