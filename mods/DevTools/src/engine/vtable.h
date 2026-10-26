// IDirect3DDevice9's functions, read off a device made for the purpose.
//
// A wrapper such as an overlay can keep the vtable inside the device and free it with the device, so
// the slots are copied while the device is alive. The game's device runs the same code.
#pragma once

#include <cstddef>

namespace DevTools::Vtable {

// The function at `slot` of IDirect3DDevice9's vtable, as declared in d3d9.h, or null when no device
// could be made.
void* Slot(size_t slot);

}
