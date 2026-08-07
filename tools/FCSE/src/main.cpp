// FCSE.exe - Far Cry Script Extender loader.
//
// Reimplements FarCry2.exe's own WinMain (see docs/docs/engine-internals/launcher-exe.md):
//   RegisterGameFunctionProvider(&RegisterDebugCommands);
//   RunGame(hInstance, cmdLine);
// both resolved from Dunia.dll by name (dunia_api.h), plus a plugin-loading step in between so
// third-party DLLs in bin\plugins\ get a chance to install hooks/patches before any Dunia.dll
// engine code beyond its own DllMain/CRT init has run. Ships as a separate exe next to the
// untouched FarCry2.exe - see tools/FCSE/README.md for the full design and install instructions.

#include "../include/plugin_api.h"
#include "caller_identity.h"
#include "debug_commands.h"
#include "dunia_api.h"
#include "function_registry.h"
#include "hook.h"
#include "log.h"
#include "mods_tab.h"
#include "patch.h"
#include "plugin_loader.h"
#include "settings_registry.h"

#include <windows.h>

namespace FCSE {
namespace {

    void __cdecl PluginLogShim(const char* message) {
        Log::FromCaller(_ReturnAddress(), message != nullptr ? message : "");
    }

    void __cdecl PluginAddFunctionCBShim(void* fn, const char* name) {
        FunctionRegistry::Register(fn, name);
    }

} // namespace
} // namespace FCSE

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE /*hPrevInstance*/, LPSTR lpCmdLine,
                    int /*nShowCmd*/) {
    using namespace FCSE;

    Log::Init(GetModuleHandleW(nullptr));
    Log::Loader("FCSE starting");

    const std::wstring& directory = Log::LoaderDirectory();

    if (!DuniaApi::Load(directory)) {
        MessageBoxW(nullptr,
                     L"FCSE could not resolve Dunia.dll next to this loader - see bin\\fcse.log "
                     L"for details.",
                     L"FCSE", MB_ICONERROR | MB_OK);
        Log::Shutdown();
        return 1;
    }

    DebugCommands::Init(directory);

    if (!HookManager::Initialize()) {
        Log::Loader("MinHook failed to initialize - tier-2 Hook() calls will fail for every "
                    "plugin this run; tier-1/tier-3 are unaffected");
    }

    // FCSE's own hook, not a plugin's - installed here so it's in place well before the player
    // could ever reach the Options screen. See mods_tab.h for the full mechanism.
    ModsTab::Install();

    // Before any plugin can register: registration resolves each setting against what this loads,
    // and fires the plugin's callback with the result, so the file has to be in memory first.
    SettingsRegistry::Init(directory + L"fcse.ini");

    FCSE_PluginAPI api{};
    api.apiVersion = FCSE_API_VERSION;
    api.duniaModule = DuniaApi::Module();
    api.duniaBase = DuniaApi::Base();
    api.duniaSize = DuniaApi::Size();
    api.Log = &PluginLogShim;
    api.AddFunctionCB = &PluginAddFunctionCBShim;
    api.Hook = &HookManager::Hook;
    api.Patch = &PatchManager::Patch;
    api.RegisterSettings = &SettingsRegistry::RegisterSettings;

    std::wstring pluginsDirectory = directory + L"plugins\\";
    PluginLoader::LoadAll(&api, pluginsDirectory);

    // Every plugin has now declared what it has, so the file can be completed in one write:
    // newly-added settings get their defaults, and a first run produces a fully hand-editable
    // fcse.ini without the player ever opening the menu.
    SettingsRegistry::Flush();

    DuniaApi::RegisterGameFunctionProvider()(reinterpret_cast<void*>(&DebugCommands::Provider));

    Log::Loader("handing off to RunGame");
    bool ok = DuniaApi::RunGame()(hInstance, lpCmdLine);
    Log::Loader(std::string("RunGame returned ") + (ok ? "true" : "false"));

    HookManager::Shutdown();
    Log::Shutdown();
    return ok ? 0 : 1;
}
