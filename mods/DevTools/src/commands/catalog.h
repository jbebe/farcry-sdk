// What the console can usefully be asked to do, as data.
//
// One row per command, so whatever drives them - an overlay, a hotkey, a script - enumerates this
// rather than hard-coding names. Only commands with something behind them in the retail build are
// listed; the ones the engine still registers but no longer implements are named in
// docs/docs/engine-internals/developer-console.md instead.
#pragma once

#include <cstddef>
#include <span>

namespace DevTools::Commands {

// What a command takes. A hint for whoever builds the control: every argument reaches the engine as
// text regardless.
enum class Arg { None, Int, Float, String, Enum };

// One option of an Enum argument: what to show, and what to send.
struct Choice {
    const char* label;
    const char* value;
};

// `line` is what gets sent, with `%s` where the argument goes, and is null for the many commands
// that send their own id - a Lua-backed one is written with the console's own `#` escape.
struct Command {
    const char* id;
    const char* name;
    const char* category;
    Arg arg;
    std::span<const Choice> choices;
    const char* line;
    const char* notes;
};

std::span<const Command> All();

const Command* Find(const char* id);

// Writes the line `command` sends, with `argument` in place of the placeholder or appended, and
// with neither when it is null or empty - which asks a setting for its current value and calls a
// Lua entry with no arguments. False only if the result does not fit.
bool Format(const Command& command, const char* argument, char* out, size_t outSize);

// Format, then queue for the end of the next frame. False means the line could not be built, never
// that the command itself did nothing.
bool Run(const Command& command, const char* argument);

}
