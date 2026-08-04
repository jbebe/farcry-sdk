#pragma once

#include "../include/plugin_api.h"

#include <string>
#include <vector>

// Backs FCSE_PluginAPI::RegisterConfigPage (tier 4) - a flat list of (plugin name, bool fields)
// pages, in registration order. FCSE's own built-in dummy page (RegisterBuiltIn below) always
// registers first, through this exact same path, so the "Mods" tab is never empty and doubles as
// the pipeline's smoke test. See mods_tab.h for how this list actually gets rendered.
namespace FCSE {

class ModsRegistry {
public:
    struct Page {
        std::string pluginName;
        std::vector<FCSE_ConfigBool> fields;
    };

    // Backs FCSE_PluginAPI::RegisterConfigPage. Captures caller identity itself via
    // _ReturnAddress(), same convention as FunctionRegistry::Register/HookManager::Hook. Returns
    // false (logged) if pluginName/fields is null or fieldCount is 0.
    static bool RegisterConfigPage(const char* pluginName, const FCSE_ConfigBool* fields,
                                    size_t fieldCount);

    // All registered pages, in registration order. Read by mods_tab.cpp each time the Options
    // page's tab list is (re)built.
    static const std::vector<Page>& Pages();

    // Registers FCSE's own single-dummy-bool page under the name "FCSE", through the exact same
    // RegisterConfigPage path a plugin would use - not a special case. Call once, before the
    // Options page's Setup() could possibly run (i.e. anywhere in main.cpp before RunGame()).
    static void RegisterBuiltIn();
};

} // namespace FCSE
