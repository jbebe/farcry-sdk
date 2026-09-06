// The game's Direct3D 9 device, as somewhere to draw.
//
// Whether the thread that presents a frame is the one the engine updates on is not assumed either
// way here, so a draw reaches the game by queueing rather than by calling. Why it is Present and not
// EndScene is in docs/docs/engine-internals/presentation-and-input.md.
#pragma once

struct IDirect3DDevice9;

namespace DevTools::Renderer {

// What to draw, called once per frame with the back buffer bound and a scene open.
using DrawFn = void (*)(IDirect3DDevice9* device);

// Called before the device is reset, which happens on a resize or an alt-tab. Everything held on
// the device has to go; the next draw builds it again.
using DeviceLostFn = void (*)();

// Takes over the device's Present and Reset. Call once from FCSE_Load. False means the overlay
// cannot draw, which it logs, and nothing is left hooked.
bool Install(DrawFn draw, DeviceLostFn onDeviceLost);

}
