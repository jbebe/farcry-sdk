// Lines drawn into the world itself, inside the frame's sky pass: the last of the world's passes,
// where the scene's depth is complete and the camera the world was drawn with is still bound. So
// they hide behind what stands in front of them, and nothing is projected on the CPU.
//
// Present comes too late for this: by then the scene's depth surface is gone. See
// docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <cstdint>
#include <vector>

namespace DevTools::WorldLines {

// One end of a line, in world coordinates, and its colour as a D3DCOLOR.
struct Vertex {
    float x;
    float y;
    float z;
    uint32_t colour;
};

// Fills `out` with the frame's lines, two vertices each. Called on the render thread, once a frame.
using FillFn = void (*)(std::vector<Vertex>& out);

// Takes over the device's EndScene. Call once from FCSE_Load. False means no lines can be drawn,
// which it logs, and nothing is left hooked.
bool Install(FillFn fill);

// Off, which it starts as, every pass goes straight through.
void SetEnabled(bool enabled);

}
