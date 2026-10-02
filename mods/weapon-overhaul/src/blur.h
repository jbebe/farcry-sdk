// The gun out of focus down the iron sights, the eye focused on the front sight.
#pragma once

#include "engine/frame.h"

namespace WeaponOverhaul::Blur {

// Draws at the end of the pass the gun's colour was drawn in, before the bloom reads the frame.
void OnScenePass(const Frame::Pass& pass);

void SetEnabled(bool enabled);

// Frees everything held on the device. Call before the device is reset.
void ReleaseDeviceObjects();

}
