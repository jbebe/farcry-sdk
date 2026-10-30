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

// What running something came to. `ran` is false when it did not run or raised an error, and `lines`
// are the lines it added, its errors among them.
struct Outcome {
    bool ran = false;
    // The value Evaluate read, as tostring renders it.
    std::string value;
    std::vector<std::string> lines;
};

// Runs `code` as one chunk, now. Game thread only. Its errors arrive as lines.
Outcome Run(const std::string& code);

// Run, from any thread, at the end of the next frame.
void Post(const std::string& code);

// Evaluates `expression` now and adds its value as a line. Game thread only.
Outcome Evaluate(const std::string& expression);

// Evaluate, from any thread, at the end of the next frame.
void PostEvaluate(const std::string& expression);

// The most recent lines, oldest first.
std::vector<std::string> Lines();

void Clear();

}
