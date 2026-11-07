// The engine's linear depth: the texture its water, soft particles and decals read the scene's depth
// from, found by watching one of them draw. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <d3d9.h>

namespace ThirdPerson::SceneDepth {

// Hooks the device's draw calls, and its reset, before which the texture is let go. False, and
// logged, when they cannot be hooked.
bool Install();

// Null until found. Holds view depth over the view distance, split across red, green and blue as 1,
// 1/256 and 1/65536 of it; zero on the first-person weapon, white where nothing was drawn. Render
// thread.
IDirect3DTexture9* Texture();

}
