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
#include "ini_file.h"
#include "log.h"
#include "lua/lua_host.h"
#include "lua/tick_source.h"
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

    // After the plugin DLLs, so a compiled plugin keeps first claim on anything contested (the
    // function registry is first-claimant-wins) and existing installs behave exactly as before.
    // Before SettingsRegistry::Flush below, so settings a script registers reach fcse.ini in the
    // same single write as the plugins'.
    // Same bin\plugins\ folder the DLLs come from - one place to install a mod, whichever form it
    // takes. The two scans cannot collide: PluginLoader takes *.dll, LuaHost takes *.lua and
    // subfolders holding a main.lua.
    LuaHost::Init(pluginsDirectory);

    // Drives the scripts' 'update' event off the engine's own frame loop. After LuaHost::Init so
    // there is an interpreter to tick.
    //
    // `Tick self check frames` under [fcse] in fcse.ini changes how many frames the one-off rate
    // check waits for (0 silences it). Read straight from the file rather than registered as a
    // setting: it is meaningless to toggle mid-run, and the Mod Configuration Menu has only 20 rows
    // to spend on things players actually want.
    {
        IniFile diagnostics;
        diagnostics.Load(directory + L"fcse.ini");
        if (const std::string* frames = diagnostics.Find("fcse", "Tick self check frames")) {
            TickSource::SetSelfCheckTicks(std::atoi(frames->c_str()));
        }
    }
    TickSource::Install();

    // Every plugin has now declared what it has, so the file can be completed in one write:
    // newly-added settings get their defaults, and a first run produces a fully hand-editable
    // fcse.ini without the player ever opening the menu.
    SettingsRegistry::Flush();

    DuniaApi::RegisterGameFunctionProvider()(reinterpret_cast<void*>(&DebugCommands::Provider));

    Log::Loader("handing off to RunGame");
    bool ok = DuniaApi::RunGame()(hInstance, lpCmdLine);
    Log::Loader(std::string("RunGame returned ") + (ok ? "true" : "false"));

    // Before the interpreter closes: a session where the frame hook never fired ends silently
    // otherwise, and that is the one outcome worth saying out loud.
    TickSource::Finish();
    LuaHost::Shutdown();
    HookManager::Shutdown();
    Log::Shutdown();
    return ok ? 0 : 1;
}
