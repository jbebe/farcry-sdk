// Cloud shadows, darkening the world from the engine's linear depth.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Shadows {

// Draws at the pass the sky is drawn in. Call after the clouds have drawn, since the shadows are
// theirs.
void OnScenePass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
