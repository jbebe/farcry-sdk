// The world around a scope at the field of view the eye has without one, as with both eyes open:
// a second, cheaper view laid down outside the eyepiece, with the scope's own view inside it.
#pragma once

#include <d3d9.h>

namespace WeaponOverhaul::Surroundings {

// For SecondView: the field of view to draw the second view at this frame, or nought.
float Want(float cameraFov);

// Lays the second view down outside the eyepiece, as the gun's colour pass begins.
void BeforeColour(IDirect3DDevice9* device);

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
