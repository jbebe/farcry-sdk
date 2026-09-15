// What the cloud shadows' passes share on top of the engine's depth: how the shadows are tuned.

#include "depth.inc.fx"

// x: the shadows' strength. y: how much of the light on the ground is the sun's.
float4 Shade : register(c96);
// xy: the blur's axis, one half-resolution pixel long. zw: one half-resolution pixel, in texture
// coordinates.
float4 Blur : register(c97);
