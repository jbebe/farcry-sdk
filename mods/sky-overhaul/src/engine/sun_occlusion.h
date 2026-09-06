// How much of the sun the player can actually see, measured against the scene.
#pragma once

struct IDirect3DDevice9;

namespace SkyOverhaul::SunOcclusion {

// Measures this frame, and must be called while the engine is drawing the sky: that is the only
// point where the scene's depth buffer is still attached. By the end of the frame it is gone, which
// is why the glare cannot simply be depth-tested where it is drawn.
void Sample(IDirect3DDevice9* device, const float sun[3]);

// Fraction of the sun's disc that reached the screen, 0 to 1. Negative when unknown - before the
// first answer arrives, or when the measurement itself came back degenerate. Callers must treat
// that as unoccluded: a measurement that cannot be trusted must not be allowed to suppress
// anything, or a silent failure here becomes a feature that silently does nothing.
float Visibility();

// The last pair of counts, for the log: how many pixels of the patch survived the depth test, and
// how many there were in total.
void LastCounts(unsigned long& reachedScreen, unsigned long& wouldHaveDrawn);

}
