// Heat haze and mirage: the world seen through the hot air over sunlit ground.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Haze {

// Bends the world at the pass the sky is drawn in, from the engine's linear depth.
void OnScenePass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
