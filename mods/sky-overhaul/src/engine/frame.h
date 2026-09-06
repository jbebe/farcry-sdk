// The engine's frame, followed from the one seam a second plugin can still take.
//
// EndScene runs once per pass on the render thread, several times a frame, so a pass has to be
// recognised rather than counted. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::Frame {

// One pass worth acting on, as the renderer finishes it.
struct Pass {
    IDirect3DDevice9* device;
    // The pass's own render target, borrowed for the length of the callback.
    IDirect3DSurface9* target;
    D3DSURFACE_DESC backBuffer;
    D3DVIEWPORT9 viewport;
    uint32_t frame;
    // Whether a world was submitted for this frame, which is false in menus and loading screens.
    bool live;
    // Whether this is the scene pass the sky is drawn in, which is the one with the world's depth
    // complete and the camera it was drawn with still bound.
    bool sky;
};

using PassFn = void (*)(const Pass&);

// Takes over the device's EndScene. Call once from FCSE_Load. `onScenePass` runs on each of the
// frame's scene passes, with the scene's own target and depth still bound; `onFinalPass` runs once
// on the composite, with the back buffer bound and the user interface not yet drawn. False means
// the frame cannot be followed, which it logs, and nothing is left hooked.
bool Install(PassFn onScenePass, PassFn onFinalPass);

}
