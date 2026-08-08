#pragma once

#include <string>

// Owns FCSE's embedded LuaJIT interpreter and the Lua mods in bin\plugins\ - the same folder the
// plugin DLLs load from, so installing a mod is one instruction regardless of what it is written in.
//
// Deliberately its own state, unrelated to the Lua that Dunia.dll itself carries. The engine embeds
// a fork of Lua 4.1-alpha - an unreleased 2002 prototype with tag methods instead of metatables and
// no pcall, require, pairs or ipairs - which no modern Lua tooling, documentation or knowledge
// applies to. FCSE ships LuaJIT (Lua 5.1 + FFI) instead, so scripts are written in the dialect the
// rest of the game-modding world already speaks.
//
// The overriding rule here is that a broken script must never take the game down with it. Every
// entry into Lua goes through a protected call with a traceback handler, and a failure costs the
// offending script its handler, not the process.
namespace FCSE {

class LuaHost {
public:
    // Creates the interpreter, installs the API, then loads and runs every script in
    // `pluginsDirectory`, firing their 'load' handlers. Returns false (logged) if the interpreter
    // could not be created; a script failing to load is not a failure of this call, since one bad
    // script should not cost the others their run.
    //
    // Call after the plugin DLLs have loaded, so a compiled plugin keeps first claim on anything
    // contested, and before any engine code runs.
    static bool Init(const std::wstring& pluginsDirectory);

    // Fires 'register_functions'. Call from the function-registry provider callback - the only point
    // at which Dunia's registry exists and accepts new names.
    static void OnRegisterFunctions();

    // Fires 'update' on every registered handler, passing the frame delta in seconds. Call once per
    // frame. `deltaSeconds` is 0 on frames where the engine's timing block is not up yet.
    static void Tick(double deltaSeconds);

    // Closes the state. Safe whether or not Init succeeded.
    static void Shutdown();

    static bool IsRunning();

    // How many scripts loaded successfully. Read by the log line after Init.
    static int LoadedScriptCount();
};

} // namespace FCSE
