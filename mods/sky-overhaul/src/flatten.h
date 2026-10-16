// Far things drawn flatter toward the eye's height, so the small world reads as farther off than it
// is. Only how it is drawn changes: what is hit, walked on and aimed at stays where it is.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Flatten {

// Takes up this frame's values, and now and then logs what could and could not be flattened.
void OnScenePass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
