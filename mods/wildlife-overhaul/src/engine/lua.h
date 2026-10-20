// The engine's Lua 4 state: run a chunk in it, and give it C functions of our own.
#pragma once

#include <string>

namespace WildlifeOverhaul::Lua {

// A Lua 4 C function: reads its arguments off `state` and returns how many results it pushed.
using CFunction = int(__cdecl*)(void* state);

// Whether this build's script system was found.
bool Found();

// Defines `name` as a global calling `function`, then runs `code` as one chunk. Game thread only.
// False when there is no script system yet, or the chunk raised an error.
bool Run(const std::string& code, const char* name, CFunction function);

}
