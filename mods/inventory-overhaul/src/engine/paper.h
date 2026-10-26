// The map's paper: the texture its "maptop" material shows, which the notebook's page replaces
// while the notebook's face is up.
#pragma once

#include <cstdint>

namespace InventoryOverhaul::Paper {

// False when the map's texture or the Direct3D device could not be hooked.
bool Install();

// Hands over a page of `size` by `size` pixels, 0xAARRGGBB, top row first, to be shown on the paper
// from the next frame on. Any thread.
void Show(const uint32_t* pixels, int size);

}
