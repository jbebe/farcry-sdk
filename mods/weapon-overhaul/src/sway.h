// The hand's drift: a few millimetres of wander and a slow breath.
#pragma once

#include "engine/aim.h"

namespace WeaponOverhaul::Sway {

// The drift this frame, for Aim::Install; nothing while switched off.
Aim::Offset Drift(float seconds);

void SetEnabled(bool enabled);

}
