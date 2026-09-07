// The device methods a plugin hooks, named without ever touching the game's own device.
#pragma once

#include <cstddef>

namespace SkyOverhaul::Vtable {

// What one IDirect3DDevice9 vtable holds: three entries from IUnknown and the rest of the
// interface. Asking for anything past this answers null rather than reading off the end.
constexpr size_t kSlotCount = 119;

// The function the game's device will call for `slot`, or null if no Direct3D device could be
// created to read it from. Every IDirect3DDevice9 in a process shares one vtable, so a device of
// our own is enough to name them all; one throwaway device is built on the first call and the
// whole table copied out while it is still alive, because a wrapper can put its vtable inside the
// object's own allocation and free it along with the device.
void* Slot(size_t slot);

// Whether `slot` really is SetVertexShaderConstantF, or SetPixelShaderConstantF when `pixel`.
// Proved by calling it on a device of our own and reading back through the interface what it
// should have written.
//
// Worth proving rather than assuming. Every other slot this plugin takes announces a wrong guess
// immediately - the frame stops arriving, or nothing draws - but a constant setter one place out
// is a call into a different function with mismatched arguments, which corrupts whatever it lands
// on and blames something else.
bool IsConstantSetter(size_t slot, bool pixel);

}
