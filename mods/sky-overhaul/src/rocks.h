// Rock and cliff lit through shaders of ours: every triangle flat, lit by the sun and the sky from
// its own facing, without the engine's sheen. Recognised by the engine's pixel shader drawing them
// and the normal map it reads; the engine's vertex shader is kept, patched to hand on its position.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Rocks {

// In the order fcse.ini lists them.
enum class Mode {
    Engine,
    Overhaul,
    // Paints rock by which of the engine's shaders draws it, and logs each pair of shaders the
    // first time and the draws through each shader every ten seconds.
    Census,
    // Draws rock as the engine does, through our pixel shaders.
    Parity,
    // Parity every other second, so that anything it gets wrong flickers.
    Blink,
    // Each triangle in the colour of its facing under a world-locked metre grid, from the position
    // our patched vertex shaders hand on.
    Grid,
};

void SetMode(Mode mode);

// Logs how many rock draws went through each kind of shader since the last time.
void OnFinalPass(const Frame::Pass& pass);

void ReleaseDeviceObjects();

}
