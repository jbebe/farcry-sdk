// The engine's rain cover map, picked out of the frame's passes: a square depth texture drawn while
// it rains, looking down along the rain over 64 m round the player, from every material that keeps
// rain off but the leaves, which src/engine/dome_draw.cpp leaves out. See
// docs/docs/engine-internals/time-of-day-and-lighting.md#rain.
#pragma once

#include "engine/camera.h"
#include "engine/frame.h"

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::RainCover {

// Call on every offscreen pass; it keeps the cover pass's texture and transform when it sees one.
void OnOffscreenPass(const Frame::Pass& pass);

struct Found {
    // A hardware shadow texture: a projected read compares and gives one in the open, nought under
    // cover. Borrowed for the frame.
    IDirect3DTexture9* texture;
    UINT size;
    // World to the map's clip space, row by row as the engine's shaders transform by.
    float transform[16];
};

// False unless the frame now being drawn drew one.
bool Latest(uint32_t frame, Found& out);

// The last one drawn, in any frame: after the rain the engine stops drawing it, and what it last
// held still says where the roofs stand round where it was drawn. False before any.
bool LastSeen(Found& out);

// What shaders/cover.inc.fx reads the last map with, for points relative to the eye: `transform` to
// the map's clip, one that puts everything beyond the map before any is drawn, and the step up the
// rain from a surface where its cover is read, with half a texel of the map in w.
void ForShaders(const Camera::View& view, float transform[16], float upRain[4]);

// Whether the engine drew the map in the last frame, which it does while it rains.
bool Drawn();

// Whether the draw about to be made goes into the map, as known from the frames before.
bool Drawing(IDirect3DDevice9* device);

// Forgets the texture. Call before the engine resets the device.
void ReleaseDeviceObjects();

}
