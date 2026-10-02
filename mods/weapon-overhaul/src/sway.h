// The hand's wiggle down the sights: the eye drifting a few millimetres off the gun.
#pragma once

#include "engine/aim.h"

namespace WeaponOverhaul::Sway {

// Where the eye sits off the gun this frame, for Aim::Install.
Aim::Offset Eye(const Aim::Frame& frame);

void SetEnabled(bool enabled);

}
