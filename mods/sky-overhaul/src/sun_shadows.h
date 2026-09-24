// The engine's own sun shadows, let follow a low sun most of the way down and reach further,
// through the engine's own shadow settings.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::SunShadows {

void SetEnabled(bool enabled);

// Holds the shadow's angles and cascade ranges against the engine's own settings, which its config
// and console can set at any time.
void OnScenePass(const Frame::Pass& pass);

}
