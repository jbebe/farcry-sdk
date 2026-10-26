// The engine's render profile, the block its graphics settings live in.
#pragma once

#include <cstdint>

namespace UFCP {

// Finds the global the profile hangs off, logging a build where it cannot. Call once from
// FCSE_Load, before anything asks for the profile.
void FindRenderProfile();

// The live profile, or null before the engine has made one or where the global was not found.
uint8_t* RenderProfile();

}
