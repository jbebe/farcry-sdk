// Grass and tree leaves lit by the sun through vertex shaders of ours, in place of the engine's.
// The engine's pixel shaders still shadow, texture and fog them.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Foliage {

void SetGrass(bool enabled);
void SetLeaves(bool enabled);

// Logs how many grass and leaf draws were lit by ours since the last time.
void OnFinalPass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
