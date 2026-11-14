// The lit beam, sent on FCSE's "flashlight.beam" channel for any plugin that wants it; README.md has the
// message.
#pragma once

#include "engine/light.h"

namespace Flashlight::Beam {

// The beam where `pose` puts it and as long and wide as `spot`. Call it every frame the light is on.
void Send(const Light::Pose& pose, const Light::Spot& spot);

// The light is off, or gone with its world; sent once, on the first call after the light was on.
void Off();

}
