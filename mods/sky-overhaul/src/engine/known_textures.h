// The engine's own textures that a draw is recognised by, named by their 16 by 16 mip level.
//
// That level is in the chain whichever top level the engine has streamed in, and among the retail
// textures that have one, its CRC-32 names each rock normal map alone. The verdict is kept on the
// texture itself, so a texture created later at a released one's address is looked at afresh.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::KnownTextures {

// Whether a texture is one of the rock normal maps.
bool IsRockNormal(IDirect3DBaseTexture9* texture);

}
