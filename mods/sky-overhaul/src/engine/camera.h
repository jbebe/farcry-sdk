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
    // distance on screen, and the same across.
    float verticalScale;
    float horizontalScale;
    // The projection's depth terms: clip depth is view depth times the scale plus the offset, and
    // w is view depth times the last.
    float depthScale;
    float depthOffset;
    float wFromDepth;

    // Where the camera is, and the directions through the viewport's four corners: top left, top
    // right, bottom left, bottom right, which is the order ScreenDraw::ClipQuad takes them in.
    // Not normalised.
    float eye[3];
    float corners[4][3];
    float direction[3];
    float right[3];
    float up[3];

    // The distance the engine's linear depth is a fraction of.
    float viewDistance;

    // The fog every sky shader applies, and the exposure the whole frame is scaled by.
    float fogColourVector[3];
    float fogColour[3];
    float fogColourRange[3];
    float fogValues[3];
    float fogHeightValues[4];
    float bloom;
};

// False when the device will not answer, which is what a pass with nothing bound yet looks like.
bool Read(IDirect3DDevice9* device, View& out);

// The depth buffer's value for a point `metres` along the camera's axis.
float BufferDepth(const View& view, float metres);

}
