// Which draw, in which pass, something on screen came from - measured rather than guessed.
//
// The black band below the far horizon is drawn after our sky by something nothing names at
// runtime. This records one whole frame at a time: every pass the frame ends, and every draw inside
// each that blends source alpha over its inverse, with the size and checksum of the shaders it was
// drawn with. Those match the objects in the game's own shader archive byte for byte, so a draw can
// be named afterwards against that archive.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::DrawCensus {

// Takes over the unindexed draw entry point and watches the indexed one through the dome's hook.
// Call once from FCSE_Load, after the dome's draw is being followed.
bool Install();

// Runs on each of the frame's scene passes, to say which passes were the world's and which the sky's.
void OnScenePass(const Frame::Pass& pass);

// Runs on the composite, which is where one measured frame ends and the wait for the next begins.
void OnFinalPass(const Frame::Pass& pass);

// Forgets which shader objects were measured, before a reset lets their addresses be reused.
void ReleaseDeviceObjects();

}
