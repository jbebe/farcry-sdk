// The engine's Lua: run a chunk, read a value back, and keep the lines the retail build drops.
//
// Script errors and System:Log both still reach a function in the shipped build, and both end there:
// nothing consumes them. They are caught here instead, along with whatever Run and Evaluate report.
// See docs/docs/engine-internals/engine-logging.md.
#pragma once

#include <string>
#include <vector>

namespace DevTools::Lua {

// Resolves the script system and hooks its error and System:Log sinks. Call once from FCSE_Load.
void Install();

// Runs `code` as one chunk at the end of the next frame. Its errors arrive as lines.
void Post(const std::string& code);

// Evaluates `expression` at the end of the next frame and adds its value as a line, as tostring
// renders it.
void PostEvaluate(const std::string& expression);

// The most recent lines, oldest first.
std::vector<std::string> Lines();

void Clear();

}
