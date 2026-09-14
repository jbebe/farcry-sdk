// The engine's linear depth: the texture its water, soft particles and decals read the scene's
// depth from, found by watching them read it.
//
// The world's own depth buffer is multisampled and cannot be sampled, and this texture has no seam
// of its own. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::DepthTexture {

// What was last seen. The texture is borrowed until ReleaseDeviceObjects.
struct Found {
    IDirect3DTexture9* texture;
    // What a texel's red, green and blue are each worth in metres, as the engine hands them to its
    // own shaders.
    float metreWeights[3];
};

// Call from inside a draw whose pixel shader reads the linear depth at s0, before the engine's own
// draw call goes through.
void Observe(IDirect3DDevice9* device);

// False until a draw has shown the texture.
bool Latest(Found& out);

// Lets go of the texture, before the engine resets the device.
void ReleaseDeviceObjects();

}
