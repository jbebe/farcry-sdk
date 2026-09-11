// The sun, as something the eye cannot look at.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Dazzle {

// Measures the sun against the world's depth on the sky pass, and paints the glare over the
// composite.
void OnScenePass(const Frame::Pass& pass);
void OnFinalPass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

}
