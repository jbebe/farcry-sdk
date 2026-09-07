// The engine's own sky dome, found by watching what the device is asked to draw.
//
// Our clouds are drawn when a pass ends, which is after everything in it - but a sky belongs under
// the sun, the moon and the stars, so it has to be drawn where the dome is drawn. There is no seam
// at the start of a pass, so the seam is the dome's own draw call. This measures what a sky pass
// draws and in what state, which is what names that one call. See
// docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::DomeDraw {

// Takes over the device's two draw entry points. Call once from FCSE_Load. False means neither
// could be taken, which it logs, and nothing is left hooked.
bool Install();

// Closes the pass being measured and decides whether to measure the next one. Runs on every scene
// pass, since a pass is only known to have been the sky's once it has ended.
void OnScenePass(const Frame::Pass& pass);

}
