// Dirt a near miss throws in the player's eye: black, see-through, blurry blobs on the side it came
// from, with the view blurred around them, clearing over a few seconds. Only what the player sees
// changes.
#pragma once

#include <d3d9.h>

#include <cstdint>

namespace AimingOverhaul::Dirt {

// A bullet someone else fired landing in the world at `point`, on the logic material whose disId is
// `material`. On the game thread.
void OnImpact(const float point[3], uint8_t material);

// Throws dirt as a round landing a metre to the right would. From any thread.
void Throw();

// Draws the dirt over the frame's finished image, `frame`, under the interface.
void OnComposite(IDirect3DDevice9* device, IDirect3DSurface9* frame);

void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
