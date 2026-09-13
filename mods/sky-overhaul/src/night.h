// The world at night as the eye sees it: colour drains to the rods' blue-grey where it is dark.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Night {

// Grades the world, and nothing drawn over the sky, at the pass the sky is drawn in.
void OnScenePass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
