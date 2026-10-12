// Game state handed to the render thread with the frame it belongs to.
//
// The render thread draws frame N while the game simulates frame N+1, so state read on the game
// thread is a frame ahead of what is on screen. The main thread posts each frame from RunFrame,
// after waiting for the previous one to finish drawing, and the render thread starts it in
// RenderLoop. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::Handoff {

// Called on the main thread as a frame is posted, with the number it is posted under.
using PostFn = void (*)(uint32_t frame);

// Hooks RunFrame and RenderLoop. False, and logged, when either is missing from this build.
bool Install(PostFn onPost);

// The number of the frame the render thread is drawing. Render thread only.
uint32_t Drawing();

}
