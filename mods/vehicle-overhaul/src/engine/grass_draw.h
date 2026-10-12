// The engine's additive pass of a spot light over grass, recognised by its vertex shaders as each
// draw is submitted, and drawn through a vertex shader of the caller's instead.
//
// The engine builds those shaders for itself, one for upright clumps and one for bent, named by
// their bytecode's CRC-32. See docs/docs/file-formats/shader-objects.md.
#pragma once

#include <d3d9.h>

namespace VehicleOverhaul::GrassDraw {

// The vertex shader to draw a spot light's grass pass with, given the engine's; null leaves the
// engine's.
using SubstituteFn = IDirect3DVertexShader9* (*)(IDirect3DDevice9* device, IDirect3DVertexShader9* engine);

// Takes over the device's DrawIndexedPrimitive. False, and logged, when it cannot be hooked.
bool Install(SubstituteFn substitute);

// Forgets every shader seen, whose addresses a new device may reuse.
void Forget();

}
