#pragma once

#include "fcse_api.h"

#include <string>
#include <vector>

// Discovers and loads plugin DLLs from bin\plugins\, and owns the two-stage lifecycle every
// plugin gets: FCSE_Load (required, called immediately after Dunia.dll is resolved - safe timing
// for Hook()/Patch() since no Dunia.dll engine code beyond its own CRT init has run yet) and
// FCSE_OnRegisterFunctions (optional, called later from within debug_commands.cpp's Provider(),
// exactly when Dunia.dll's function registry is guaranteed constructed).
namespace FCSE {

class PluginLoader {
public:
    // Creates `pluginsDirectory` if missing, then loads every *.dll in it and calls FCSE_Load(api)
    // on each. A plugin missing the FCSE_Load export, or whose FCSE_Load returns false, is
    // unloaded and skipped (logged either way). Retains the api pointer and every successfully
    // loaded plugin's optional FCSE_OnRegisterFunctions for RunOnRegisterFunctions() below.
    static void LoadAll(const FCSE_PluginAPI* api, const std::wstring& pluginsDirectory);

    // Calls every successfully loaded plugin's FCSE_OnRegisterFunctions (plugins that didn't
    // export one are skipped), in load order. Must only be called from within the
    // RegisterGameFunctionProvider callback (see debug_commands.h), and before FCSE's own stock
    // registrations, so a plugin can claim a name first.
    static void RunOnRegisterFunctions();

    // Every successfully loaded plugin's name, in load order - the same module-derived name used to
    // tag its log lines. This is what the in-game menu lists, so a plugin appears there whether or
    // not it registered any settings; the settings registry only knows about the ones that did.
    static const std::vector<std::string>& LoadedNames();
};

} // namespace FCSE
