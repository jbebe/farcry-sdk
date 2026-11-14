// The engine's own Direct3D 9 device class, whose methods every world draw and every pass end go
// through. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <d3d9.h>

namespace AimingOverhaul::EngineDevice {

// The device the game created, which under a wrapper such as ReShade or dgVoodoo is the wrapper's.
IDirect3DDevice9* Direct3D(void* self);

// Whether the engine's constant upload was found; a draw hook that reads constants needs it.
bool CanSendConstants();

// Sends the shader constants the engine has changed since its last draw, so the device holds what
// the next draw will see. The engine's own draw then has nothing left to send.
void SendConstants(IDirect3DDevice9* device);

}
