#pragma once

// Wraps Dunia.dll's real AddFunctionCB with a name-ownership map, so a second registration of an
// already-claimed name is a loud, logged rejection instead of Dunia's own silent no-op (confirmed
// via decompile of FunctionRegistry_Insert, 0x10299430: it's a find-first insert, the existing
// entry is never overwritten - first registrant for a name always wins at the engine level).
//
// Used both by plugins (through FCSE_PluginAPI::AddFunctionCB, tier 1 of the plugin API) and by
// FCSE's own stock RegisterDebugCommands reimplementation (debug_commands.cpp) - both go through
// this same Register() so ownership/conflict tracking covers the stock handlers too. Because
// plugins' FCSE_OnRegisterFunctions all run before the stock registrations (see main.cpp), a
// plugin registering e.g. "AddDiamond" first means the later stock registration for that same
// name is the one that gets rejected - that's how a plugin overrides a stock handler.
namespace FCSE {

class FunctionRegistry {
public:
    // Call only after Dunia.dll has invoked the function-registry provider callback (i.e. from
    // within RegisterDebugCommands or a plugin's FCSE_OnRegisterFunctions) - g_pFunctionRegistry
    // is not guaranteed constructed before that point. Captures caller identity itself via
    // _ReturnAddress() (works uniformly whether the caller is FCSE.exe's own code or a plugin
    // DLL).
    static bool Register(void* fn, const char* name);
};

} // namespace FCSE
