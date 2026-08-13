#include "api/plugin_loader.h"

#include "caller_identity.h"
#include "log.h"
#include "util/dir_walk.h"
#include "util/win_string.h"

#include <windows.h>

#include <vector>

namespace FCSE {

namespace {
    // Only the callback is retained per plugin. The HMODULE is deliberately never released - a
    // plugin's hooks and patches outlive FCSE's own loading phase - and nothing needs the handle
    // back to arrange that, so there is nothing else worth keeping.
    std::vector<FCSE_OnRegisterFunctionsFn> g_onRegisterCallbacks;
    std::vector<std::string> g_loadedNames;
    const FCSE_PluginAPI* g_api = nullptr;

    // Collects every *.dll under `directory`, at any depth, so a mod can ship as a folder holding
    // its DLL alongside whatever else it needs rather than scattering files into a shared
    // bin\plugins\ root.
    //
    // Everything found is offered to LoadLibraryW, including DLLs a mod ships purely as its own
    // dependencies. That is deliberate rather than an oversight: a dependency has no FCSE_Load
    // export, so it is unloaded again and logged as skipped, and the alternative - guessing which
    // DLLs "look like" plugins from their names - would be wrong in both directions.
    std::vector<std::wstring> CollectDlls(const std::wstring& directory) {
        std::vector<std::wstring> found;
        WalkDirectory(directory, [&found](const std::wstring& path, const std::wstring& name) {
            if (HasExtensionI(name, L".dll")) {
                found.push_back(path);
            }
        });
        return found;
    }
}

void PluginLoader::LoadAll(const FCSE_PluginAPI* api, const std::wstring& pluginsDirectory) {
    g_api = api;

    // Fine if it already exists; a missing plugins\ folder just means "no plugins installed yet".
    CreateDirectoryW(pluginsDirectory.c_str(), nullptr);

    const std::vector<std::wstring> pluginPaths = CollectDlls(pluginsDirectory);
    if (pluginPaths.empty()) {
        Log::Loader("no plugin DLLs found in " + Narrow(pluginsDirectory));
        return;
    }

    for (const std::wstring& pluginPath : pluginPaths) {
        HMODULE hPlugin = LoadLibraryW(pluginPath.c_str());
        if (hPlugin == nullptr) {
            Log::Loader("failed to load plugin " + Narrow(pluginPath) + ", LoadLibraryW error " +
                        std::to_string(GetLastError()));
            continue;
        }

        std::string name = ResolveCallerModuleName(reinterpret_cast<void*>(hPlugin));

        auto loadFn = reinterpret_cast<FCSE_LoadFn>(GetProcAddress(hPlugin, "FCSE_Load"));
        if (loadFn == nullptr) {
            Log::Loader("plugin '" + name + "' has no FCSE_Load export, skipped");
            FreeLibrary(hPlugin);
            continue;
        }

        if (!loadFn(api)) {
            Log::Loader("plugin '" + name + "' FCSE_Load returned false, unloading");
            FreeLibrary(hPlugin);
            continue;
        }

        auto onRegister = reinterpret_cast<FCSE_OnRegisterFunctionsFn>(
            GetProcAddress(hPlugin, "FCSE_OnRegisterFunctions"));

        if (onRegister != nullptr) {
            g_onRegisterCallbacks.push_back(onRegister);
        }
        g_loadedNames.push_back(name);
        Log::Loader("plugin '" + name + "' loaded" +
                    (onRegister == nullptr ? " (no FCSE_OnRegisterFunctions export)" : ""));
    }
}

void PluginLoader::RunOnRegisterFunctions() {
    for (FCSE_OnRegisterFunctionsFn onRegister : g_onRegisterCallbacks) {
        onRegister(g_api);
    }
}

const std::vector<std::string>& PluginLoader::LoadedNames() { return g_loadedNames; }

} // namespace FCSE
