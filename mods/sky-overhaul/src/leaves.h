// Tree leaves lit by the sun through vertex shaders of ours, in place of the engine's: the crown
// shades its own far side from the sun, each leaf tilts its own way, and the glint off leaves facing
// the sun reaches further. The engine's pixel shaders still shadow, texture and fog them.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Leaves {

void SetEnabled(bool enabled);

// Logs how many leaf draws were lit by ours since the last time.
void OnFinalPass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
