// A faint rainbow opposite a low sun as the rain here clears, 130 of its 180 degrees from the left
// or the right foot, over the sky down to the skyline.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Rainbow {

// Draws into the sky pass after the clouds, while the weather has a rainbow and sun reaches the eye.
void OnScenePass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
