// A hit on the player, as they see it: on the body it punches the view back and tips it down and
// darkens it from the edges; on a vest it only punches, shorter; on the head it cuts
// the view to black and brings it back double, the second image fading slowly.
#pragma once

#include <d3d9.h>

namespace AimingOverhaul::Hit {

enum class Where { kBody, kHead, kVest };

// A hit that took `lost` of the player's health, from nought to one. On the game thread.
void OnHit(bool head, float lost);

// Shows a hit at `where` as hard as the hardest. From any thread.
void Try(Where where);

// How far a hit's punch moves the eye: back, in metres, and pitched up, in radians.
struct Punch {
    float back;
    float pitch;
};

// Once a frame on foot, with the seconds since the last: the punch this frame, none while a
// scope's sight picture is up.
Punch Update(float seconds);

// Draws the vignette, the black and the double image over the frame's finished image, `frame`,
// under the interface.
void OnComposite(IDirect3DDevice9* device, IDirect3DSurface9* frame);

void ReleaseDeviceObjects();

void SetEnabled(bool enabled);

}
