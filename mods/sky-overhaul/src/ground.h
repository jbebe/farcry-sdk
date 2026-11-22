// The world wet from rain over the finished picture: mud, and puddles that mirror the world and ring
// with the drops, while the materials' own shine is raised draw by draw in src/wet.cpp.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Ground {

// Follows the Rain switch.
void SetEnabled(bool enabled);

// Draws into the sky pass, before the rain falls over it.
void OnScenePass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
