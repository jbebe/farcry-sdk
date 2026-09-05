// Far Cry 2's own developer console, driven from code instead of from the `~` prompt.
//
// Everything the console can reach is reachable here, on the same terms: a line runs as if it had
// been typed, `#` still escapes to Lua, and the commands hidden behind the developer flag run
// whatever the Developer console option is set to. What the engine does behind that is in
// docs/docs/engine-internals/developer-console.md.
#pragma once

namespace DevTools::Console {

// Resolves the console and the calls into it. Call once from FCSE_Load, after FCSE::Bind and only
// if the game thread was hooked. On failure it logs what was missing and everything below becomes
// a no-op.
void Install();

// Installed, the console is up, and this is the game thread - what the two calls below need.
bool IsReady();

// Runs one line as the console would, developer-only commands included. Returns whether the line
// was handed over; the console answers for the command itself, unknown names included.
bool Execute(const char* line);

// Writes one line to the console's own output.
void Print(const char* format, ...);

// Runs a line from anywhere: the text is copied and executed at the end of the next frame, which is
// also the safe point to ask for something that tears the world down.
void PostLine(const char* line);

}
