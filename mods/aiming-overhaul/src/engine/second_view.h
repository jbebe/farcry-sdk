// A second view of the world each frame, from the player's camera at a field of view of our own,
// drawn by the engine's water reflection renderer right after it draws the water's reflection.
//
// See docs/docs/engine-internals/presentation-and-input.md for the reflection pass.
#pragma once

#include <d3d9.h>

namespace AimingOverhaul::SecondView {

// Asked once a frame with the field of view the player's camera has, in radians; answers the field
// of view to draw the second view at, or nought for none this frame.
using WantFn = float (*)(float cameraFov);

// Takes over the moment after the reflection pass. False, and logged, when the renderer is not
// found in this build.
bool Install(WantFn want);

// The second view drawn this frame, null if there is none. Borrowed until ReleaseDeviceObjects.
IDirect3DTexture9* Latest();

void ReleaseDeviceObjects();

}
