// A vertex shader's bytecode rewritten to also hand its camera-relative position to the pixel
// shader, which the engine's own shaders keep to themselves.
#pragma once

#include <windows.h>

#include <vector>

namespace SkyOverhaul::VertexPatch {

// `tokens` with the temp its clip position is projected from written, as the shader's last
// instruction, to the output declared TEXCOORD`semantic`, which is declared if it was not. Empty if
// the clip position is not four dp4 of one temp into o0, which is how every Generic shader writes it.
std::vector<DWORD> WithPosition(const DWORD* tokens, size_t count, UINT semantic);

}
