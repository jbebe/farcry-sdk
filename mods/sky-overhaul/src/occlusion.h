// Ambient occlusion from a depth of the world without its grass and leaves, multiplied into the
// world at the sky pass. Foliage casts none, and takes the occlusion of the ground at its roots.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Occlusion {

// Draws at the pass the sky is drawn in. Call after SolidDepth::OnScenePass, which ends the depth
// it reads, and after the cloud shadows, which darken the same light.
void OnScenePass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

// Also turns the solid depth on or off.
void SetEnabled(bool enabled);

}
