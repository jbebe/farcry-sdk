// Rock and cliff lit through shaders of ours: every triangle flat, lit by the sun and the sky from
// its own facing, with a detail map laid over it and without the engine's sheen. Recognised by the
// engine's pixel shader drawing them and the normal map it reads; the engine's vertex shader is
// kept, patched to hand on its position.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Rocks {

void SetEnabled(bool enabled);

// Logs how many rock draws went through each of the engine's shaders since the last time.
void OnFinalPass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
