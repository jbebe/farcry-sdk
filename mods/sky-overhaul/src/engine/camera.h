// The camera and the world's fog, read back out of the constants the engine leaves bound.
//
// Only the world's own passes have these; at the composite they are stale. See
// docs/docs/engine-internals/sky-and-clouds.md for what the engine puts in which register.
#pragma once

#include <d3d9.h>

namespace SkyOverhaul::Camera {

struct View {
    // World to clip, as the engine's own shaders transform by, row by row.
    float viewProjection[16];
    // One over the tangent of half the vertical field of view, which turns an angle into a
    // distance on screen.
    float verticalScale;

    // Where the camera is and the directions through the viewport's four corners, both derived
    // from the view-projection alone: top left, top right, bottom left, bottom right, which is
    // the order ScreenDraw::ClipQuad takes them in. The corners are not normalised.
    float eye[3];
    float corners[4][3];

    // The same four directions built from the camera basis instead, which needs no inversion and
    // no arithmetic on world-sized numbers. The two should agree; where they do not, this one is
    // the better conditioned.
    float basisCorners[4][3];

    // The same camera as the engine describes it, which is the cross-check on the inverse above.
    float direction[3];
    float right[3];
    float up[3];
    float position[3];
    float viewPoint[3];

    // The fog every sky shader applies, and the exposure the whole frame is scaled by.
    float fogColourVector[3];
    float fogColour[3];
    float fogColourRange[3];
    float fogValues[3];
    float fogHeightValues[4];
    float bloom;
};

// False when the device will not answer or the transform cannot be inverted, which is what a pass
// with nothing bound yet looks like.
bool Read(IDirect3DDevice9* device, View& out);

}
