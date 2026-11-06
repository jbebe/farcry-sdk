// The frame's sky pass: the last of the world's passes, where the scene's depth is complete, the
// camera the world was drawn with is still bound, and the first-person weapon is not yet drawn. See
// docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <d3d9.h>

namespace PlayerBody::SkyPass {

// The camera, in the world: z up.
struct View {
    float eye[3];
    float forward[3];
    float right[3];
    float up[3];
    // Half the view's width and height one metre ahead.
    float halfWidth;
    float halfHeight;
    // What the linear depth is a fraction of, in metres.
    float viewDistance;
};

struct Pass {
    IDirect3DDevice9* device;
    D3DVIEWPORT9 viewport;
    View view;
};

using PassFn = void (*)(const Pass& pass);

// Hooks the device's EndScene and Present. `onPass` runs once a frame at most, on the render thread,
// with the pass's target and depth bound. False when they cannot be hooked, which is logged.
bool Install(PassFn onPass);

}
