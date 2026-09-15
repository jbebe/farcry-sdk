// A depth of the world without its grass and leaves, at half resolution: every draw that writes the
// world's depth before the sky pass, other than foliage and the first-person weapon, is drawn a
// second time through the engine's own shaders into a depth texture of our own.
//
// Hardware depth rather than the engine's packed linear depth, so it steps by micrometres up close
// instead of centimetres. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include "engine/frame.h"

#include <d3d9.h>

namespace SkyOverhaul::SolidDepth {

// What the frame just ended gathered. The texture is borrowed until ReleaseDeviceObjects, and holds
// one where nothing was drawn.
struct Found {
    IDirect3DTexture9* texture;
    UINT width;
    UINT height;
};

// Call before each of the engine's draws. True when the draw belongs in the solid depth and the
// device now draws into it, in which case the caller draws once more and then calls End.
bool Begin(IDirect3DDevice9* device);

// Puts back the target, depth surface, viewport and scissor Begin replaced.
void End(IDirect3DDevice9* device);

// The sky pass ends the frame's gathering, before anything reads Latest; the composite starts the
// next frame's.
void OnScenePass(const Frame::Pass& pass);
void OnFinalPass(const Frame::Pass& pass);

// False unless the frame just ended drew into it.
bool Latest(Found& out);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
