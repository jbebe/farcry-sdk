// The command line the engine parses, with switches added to it.
//
// Two options are settings here and flags there: the engine already reads `-borderless` and
// `-RenderProfile_MaxFps`, and reads both once, while it is starting. RunGame is the last moment
// anything can still change what it sees.
#pragma once

namespace UFCP {

// Sets `-name value` on the command line the engine will parse, `-name` alone when `value` is
// empty, and removes the switch when `value` is null. The last call for a name wins, so an option
// registered twice before launch gets what it asked for the second time. A switch the player passed
// themselves is never overridden.
void SetCommandLineSwitch(const char* name, const char* value);

// Detours Dunia's RunGame export, which is what carries the switches above into the engine and what
// identifies the engine's own thread. Call once from FCSE_Load.
void InstallRunGameHook();

// True on the thread that called RunGame, which is the one the engine runs on. False until it has,
// and false elsewhere - a settings callback can arrive on either.
bool IsEngineThread();

}
