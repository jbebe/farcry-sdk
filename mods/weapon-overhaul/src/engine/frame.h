// The engine's frame, followed pass by pass from EndScene.
//
// See docs/docs/engine-internals/presentation-and-input.md for the passes and how they are told
// apart.
#pragma once

#include <cstdint>
#include <d3d9.h>

namespace WeaponOverhaul::Frame {

// One of the world's passes, as it ends.
struct Pass {
    IDirect3DDevice9* device;
    // The pass's render target, borrowed for the callback.
    IDirect3DSurface9* target;
    uint32_t serial;
};

using PassFn = void (*)(const Pass&);

// Takes over the device's EndScene; `onScenePass` runs as each of the world's passes ends, with its
// target and depth still bound. False, and logged, when it cannot be hooked.
bool Install(PassFn onScenePass);

// How many passes have ended so far, so two draws with the same serial are in the same pass.
uint32_t PassSerial();

// Counts frames, stepping as the composite ends one.
uint32_t Number();

// Whether this frame's sky pass has ended, which puts a draw after the whole world.
bool PastSky();

// The back buffer's size as the last pass found it.
UINT Width();
UINT Height();

}
