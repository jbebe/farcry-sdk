// The engine's draws of grass lit by the sun, recognised by their shaders as they are submitted, with
// room for a draw of the same grass straight after each.
//
// The lit vertex shaders are ones the engine builds for itself, named by their bytecode's CRC-32.
// See docs/docs/file-formats/shader-objects.md.
#pragma once

#include <d3d9.h>

namespace VehicleOverhaul::GrassDraw {

// Draws the grass draw that has just been made once more, with whatever is bound now.
using RedrawFn = HRESULT (*)(IDirect3DDevice9* device);

// Whether to look at the draws at all, asked on every indexed draw, so it has to be cheap.
using WantedFn = bool (*)();

// Called after each lit grass draw whose placement matches grass_light.fx, with the device as that
// draw left it.
using AfterFn = void (*)(IDirect3DDevice9* device, RedrawFn redraw);

// Takes over the device's DrawIndexedPrimitive. False, and logged, when it cannot be hooked.
//
// While `wanted` holds, the vertex shaders of the grass draws it cannot follow are written to
// VehicleOverhaul-shaders beside the plugin once each, and logged, so they can be read.
bool Install(WantedFn wanted, AfterFn after);

// Forgets every shader seen, whose addresses a new device may reuse.
void Forget();

}
