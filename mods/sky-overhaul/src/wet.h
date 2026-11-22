// The world wet from rain, through each material's own lighting: its specular raised and sharpened
// as the ground wets, and water roughened as the rain comes down harder.
#pragma once

#include "engine/dome_draw.h"

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::Wet {

// How wet the open world is, how hard it rains and how full the puddles are, each nought to one, for
// the draws that follow.
void Set(float wetness, float rain, float puddles);
float Wetness();
float Rain();
float Puddles();

// DomeDraw's shine change.
void Shine(IDirect3DDevice9* device, const uint8_t* shine, DomeDraw::ShineSaved& saved);

}
