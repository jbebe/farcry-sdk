// Copies of the frame, shared by every effect that reads the pixels it overwrites.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::FrameCopy {

// The pass's target as it stands now, single-sampled; the next call for that format overwrites it.
// Null if the device will not make or fill it, which it logs once per format.
IDirect3DTexture9* Take(const Frame::Pass& pass);

// Frees the copies. Call before the engine resets the device.
void ReleaseDeviceObjects();

}
