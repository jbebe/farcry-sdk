// The player's health in the picture: at 3 bars it loses colour, at 2 more of it with grain and a
// slight glow, on the last bar it is black and white, grainy and glowing; each fades into the next.
#pragma once

#include <d3d9.h>

namespace AimingOverhaul::Health {

// The player's health, from nought to one, on the game thread: each frame on foot, and after each
// hit wherever the player is.
void Set(float health);

// Over the finished frame, below a hit's darkening.
void OnComposite(IDirect3DDevice9* device, IDirect3DSurface9* frame);

// Shows a level whatever the health, 1 for 3 bars to 3 for the last bar; 0 follows the health again.
void Show(int level);
int Shown();

void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
