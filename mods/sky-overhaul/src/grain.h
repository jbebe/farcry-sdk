// Film grain over the finished frame.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Grain {

// Paints the grain over the composite, under the heads-up display.
void OnFinalPass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
