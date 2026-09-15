// The engine's frame, followed from the one seam a second plugin can still take.
//
// EndScene runs once per pass on the render thread, several times a frame, so a pass has to be
// recognised rather than counted. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::Frame {

// The sky pass is the only one of the frame's scene passes whose depth range is squeezed against
// the far plane, which is how it is recognised. Shared, because anything watching the draws inside
// a pass rather than the passes themselves has to recognise it the same way.
constexpr float kSkyPassMinZ = 0.9f;

// The far end of the depth range, which the sky holds exactly: the world's geometry is all nearer,
// at any distance, and a depth short of one would stop telling the two apart somewhere down the view.
constexpr float kFarDepth = 1.0f;

// One pass worth acting on, as the renderer finishes it.
struct Pass {
    IDirect3DDevice9* device;
    // The pass's own render target and depth-stencil surface, borrowed for the length of the
    // callback. The composite has no depth surface.
    IDirect3DSurface9* target;
    IDirect3DSurface9* depth;
    D3DSURFACE_DESC backBuffer;
    D3DVIEWPORT9 viewport;
    uint32_t frame;
    // Whether a world was submitted for this frame, which is false in menus and loading screens.
    bool live;
    // Whether this is the frame's first scene pass the sky is drawn in, which is the one with the
    // world's depth complete and the camera it was drawn with still bound. A menu frame can hold a
    // second, and an effect drawn into both would lay itself over itself.
    bool sky;
};

using PassFn = void (*)(const Pass&);

// Takes over the device's EndScene. Call once from FCSE_Load. `onScenePass` runs on each of the
// frame's scene passes, with the scene's own target and depth still bound; `onFinalPass` runs once
// on the composite, with the back buffer bound and the user interface not yet drawn. False means
// the frame cannot be followed, which it logs, and nothing is left hooked.
bool Install(PassFn onScenePass, PassFn onFinalPass);

// How many passes have ended so far. What a draw is in the middle of belongs to the pass that has
// not ended yet, so this is the only thing that says two draws are in the same one - and a frame
// can hold more than one sky pass, which makes the frame number the wrong answer.
uint32_t PassSerial();

}
