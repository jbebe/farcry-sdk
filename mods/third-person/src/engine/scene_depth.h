// The engine's linear depth: the texture its water, soft particles and decals read the scene's depth
// from, which each view hands its shaders as DepthVPSampler. See
// docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <d3d9.h>

namespace ThirdPerson::SceneDepth {

// Hooks the renderer's per-view parameter update, which holds the view's depth. False, and logged,
// when it cannot be hooked.
bool Install();

// Null while no view with depth has been drawn, or when it is not a render target of this size.
// Holds view depth over the view distance, split across red, green and blue as 1, 1/256 and 1/65536
// of it; zero on the first-person weapon, white where nothing was drawn. No reference is held.
// Render thread.
IDirect3DTexture9* Texture(UINT width, UINT height);

}
