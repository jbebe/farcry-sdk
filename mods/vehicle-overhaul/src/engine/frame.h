// The engine's frame on the render thread, followed pass by pass from EndScene.
//
// EndScene runs once per pass, several times a frame, so a pass has to be recognised rather than
// counted. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <d3d9.h>

namespace VehicleOverhaul::Frame {

struct Pass {
    IDirect3DDevice9* device;
    D3DVIEWPORT9 viewport;
};

using PassFn = void (*)(const Pass&);

// Takes over the device's EndScene. `onSkyPass` runs as the frame's first sky pass ends, the one
// with the world's depth complete and the camera it was drawn with still bound; `onComposite` runs
// as the tone-mapped image is finished in the back buffer, under the user interface. False, and
// logged, when it cannot be hooked.
bool Install(PassFn onSkyPass, PassFn onComposite);

}
