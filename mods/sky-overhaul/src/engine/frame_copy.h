// One copy of the composite, shared by every effect that reads the pixels it overwrites.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::FrameCopy {

// The pass's target as it stands now, in a texture the next call overwrites. Null if the device
// will not make or fill it.
IDirect3DTexture9* Take(const Frame::Pass& pass);

// Frees the copy. Call before the engine resets the device.
void ReleaseDeviceObjects();

}
