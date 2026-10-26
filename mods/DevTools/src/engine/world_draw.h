// Shapes drawn into the world itself, inside the frame's sky pass: the last of the world's passes,
// where the scene's depth is complete and the camera the world was drawn with is still bound, so
// they hide behind what stands in front of them. See
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

// Takes over the device's EndScene. Call once from FCSE_Load; logs if it cannot.
void Install();

// What to draw each frame. Null, which it starts as, draws nothing and costs every pass one test.
void SetFill(FillFn fill);

}
