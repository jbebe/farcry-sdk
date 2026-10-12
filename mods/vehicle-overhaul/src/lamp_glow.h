// Every lit headlamp as a bright point: a lit white plate from afar, and a glare that dazzles a
// little up close in front of it. The lamps are measured against the world's depth on the sky pass
// and drawn over the finished frame.
#pragma once

#include "engine/frame.h"

namespace VehicleOverhaul::LampGlow {

void OnSkyPass(const Frame::Pass& pass);
void OnComposite(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

}
