// A temporary trace of whole frames while settled into the sights: every pass, and what its draws
// went through. To be removed once the gun's passes are documented.
#pragma once

#include <d3d9.h>

namespace WeaponOverhaul::Trace {

// Hooks DrawPrimitive, which only the trace counts. False, and logged, when it cannot.
bool Install();

// At every EndScene, before the pass serial steps.
void Pass(IDirect3DDevice9* device, bool composite, bool pastSky);

// At every indexed draw.
void IndexedDraw(IDirect3DDevice9* device);

// At a draw outside the gun's depth pass of a part that pass drew.
void GunPart();

}
