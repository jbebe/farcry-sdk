// Shapes drawn into the world itself, inside the frame's sky pass: the last of the world's passes,
// where the scene's depth is complete and the camera the world was drawn with is still bound. So
// they hide behind what stands in front of them, and nothing is projected on the CPU.
//
// Present comes too late for this: by then the scene's depth surface is gone. See
// docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <cstdint>
#include <vector>

namespace DevTools::WorldDraw {

// A point in world coordinates and its colour as a D3DCOLOR, alpha included.
struct Vertex {
    float x;
    float y;
    float z;
    uint32_t colour;
};

// One frame's shapes: lines as vertex pairs, drawn a few pixels wide facing the camera, and
// translucent triangles as vertex triples, drawn under the lines.
struct Frame {
    std::vector<Vertex> lines;
    std::vector<Vertex> triangles;
};

// Fills the frame's shapes. Called on the render thread, once a frame, with both lists empty.
using FillFn = void (*)(Frame& out);

// Takes over the device's EndScene. Call once from FCSE_Load. False means nothing can be drawn,
// which it logs, and nothing is left hooked.
bool Install(FillFn fill);

// Off, which it starts as, every pass goes straight through.
void SetEnabled(bool enabled);

}
