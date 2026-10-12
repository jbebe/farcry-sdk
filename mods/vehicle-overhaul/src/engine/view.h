// The camera the world was drawn with, and its fog, read back out of the vertex shader constants the
// engine leaves bound. Only the world's own passes have these; at the composite they are stale.
#pragma once

#include <d3d9.h>

namespace VehicleOverhaul::View {

struct Camera {
    // World to clip, row by row: clip x, y, z and w are each a row dotted with the point.
    float viewProjection[16];
    float eye[3];
    // One over the tangent of half the vertical field of view.
    float verticalScale;
    // The fog every world shader applies, as FogAmount in grass_light.fx reads it.
    float fogValues[3];
    float fogHeightValues[4];
};

// False when the device will not answer.
bool Read(IDirect3DDevice9* device, Camera& out);

// How much of a point's colour the engine's fog leaves, 0 to 1.
float Clear(const Camera& camera, const float* point);

}
