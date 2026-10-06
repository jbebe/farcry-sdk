// A PNG embedded in the plugin as a resource, made into a texture.
#pragma once

#include <d3d9.h>

namespace AimingOverhaul::EmbeddedImage {

// The PNG embedded under `name`, its colour multiplied by its cover, in the managed pool with its
// whole mip chain; null, and logged, when it is missing, cannot be read or the device refused it.
IDirect3DTexture9* Texture(IDirect3DDevice9* device, const char* name);

}
