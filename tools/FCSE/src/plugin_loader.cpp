#include "plugin_loader.h"

#include "caller_identity.h"
#include "log.h"

#include <windows.h>

#include <vector>

namespace FCSE {

namespace {
    struct LoadedPlugin {
        HMODULE module;
        std::string name;
        FCSE_OnRegisterFunctionsFn onRegister;
    };

    std::vector<LoadedPlugin> g_plugins;
    std::vector<std::string> g_loadedNames; // parallel to g_plugins, for LoadedNames()
    const FCSE_PluginAPI* g_api = nullptr;

    std::string Narrow(const std::wstring& wide) {
        int len = WideCharToMultiByte(CP_ACP, 0, wide.c_str(), static_cast<int>(wide.size()),
                                       nullptr, 0, nullptr, nullptr);
        if (len <= 0) {
            return "";
        }
        std::string result(len, '\0');
        WideCharToMultiByte(CP_ACP, 0, wide.c_str(), static_cast<int>(wide.size()), result.data(),
                             len, nullptr, nullptr);
        return result;
    }
}

void PluginLoader::LoadAll(const FCSE_PluginAPI* api, const std::wstring& pluginsDirectory) {
    g_api = api;

    // Fine if it already exists; a missing plugins\ folder just means "no plugins installed yet".
    CreateDirectoryW(pluginsDirectory.c_str(), nullptr);

    std::wstring pattern = pluginsDirectory + L"*.dll";
    WIN32_FIND_DATAW findData;
    HANDLE hFind = FindFirstFileW(pattern.c_str(), &findData);
    if (hFind == INVALID_HANDLE_VALUE) {
        Log::Loader("no plugin DLLs found in " + Narrow(pluginsDirectory));
        return;
    }

    do {
        if (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            continue;
        }

        std::wstring pluginPath = pluginsDirectory + findData.cFileName;
        HMODULE hPlugin = LoadLibraryW(pluginPath.c_str());
        if (hPlugin == nullptr) {
            Log::Loader("failed to load plugin " + Narrow(findData.cFileName) + ", LoadLibraryW error " +
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

        g_plugins.push_back({hPlugin, name, onRegister});
        g_loadedNames.push_back(name);
        Log::Loader("plugin '" + name + "' loaded" +
                    (onRegister == nullptr ? " (no FCSE_OnRegisterFunctions export)" : ""));
    } while (FindNextFileW(hFind, &findData));

    FindClose(hFind);
}

void PluginLoader::RunOnRegisterFunctions() {
    for (const LoadedPlugin& plugin : g_plugins) {
        if (plugin.onRegister != nullptr) {
            plugin.onRegister(g_api);
        }
    }
}

const std::vector<std::string>& PluginLoader::LoadedNames() { return g_loadedNames; }

} // namespace FCSE
