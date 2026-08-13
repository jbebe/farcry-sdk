// FCSE.exe - Far Cry Script Extender loader.
//
// Reimplements FarCry2.exe's own WinMain (see docs/docs/engine-internals/launcher-exe.md):
//   RegisterGameFunctionProvider(&RegisterDebugCommands);
//   RunGame(hInstance, cmdLine);
// both resolved from Dunia.dll by name (dunia_api.h), plus a plugin-loading step in between so
// third-party DLLs in bin\plugins\ get a chance to install hooks/patches before any Dunia.dll
// engine code beyond its own DllMain/CRT init has run. Ships as a separate exe next to the
// untouched FarCry2.exe - see tools/FCSE/README.md for the full design and install instructions.

#include "api/hook.h"
#include "api/plugin_api.h"
#include "api/plugin_loader.h"
#include "api/settings_registry.h"
#include "crash_log.h"
#include "engine/address_library.h"
#include "engine/build_id.h"
#include "engine/debug_commands.h"
#include "engine/dunia_api.h"
#include "engine/splash.h"
#include "loader_paths.h"
#include "log.h"
#include "lua/lua_host.h"
#include "lua/tick_source.h"
#include "ui/mods_tab.h"

#include <windows.h>

namespace FCSE {
namespace {

    // Handed to Dunia.dll via RegisterGameFunctionProvider, matching what FarCry2.exe's own WinMain
    // passes. Dunia.dll invokes it exactly once, after InitDuniaEngine succeeds - the only point at
    // which the function registry is guaranteed to exist.
    //
    // The order is the whole override mechanism: FunctionRegistry_Insert is first-claimant-wins, so
    // plugins claim names before scripts, and both before FCSE's own stock handlers.
    void __cdecl ProvideGameFunctions() {
        Log::Loader("provider callback invoked by Dunia.dll - running plugin registrations, then "
                    "stock handlers");

        PluginLoader::RunOnRegisterFunctions();
        LuaHost::OnRegisterFunctions();
        DebugCommands::RegisterStockHandlers();
    }

} // namespace
} // namespace FCSE

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE /*hPrevInstance*/, LPSTR lpCmdLine,
                    int /*nShowCmd*/) {
    using namespace FCSE;

    LoaderPaths::Init(GetModuleHandleW(nullptr));
    Log::Init(LoaderPaths::Directory());
    Log::Loader("FCSE starting");

    // Immediately after the log opens and before anything else: this is what turns a
    // crash-to-desktop into an address, and the things most likely to fault are the engine
    // touchpoints installed below.
    CrashLog::Install();

    const std::wstring& directory = LoaderPaths::Directory();

    if (!DuniaApi::Load(directory)) {
        MessageBoxW(nullptr,
                     L"FCSE could not resolve Dunia.dll next to this loader - see bin\\fcse.log "
                     L"for details.",
                     L"FCSE", MB_ICONERROR | MB_OK);
        Log::Shutdown();
        return 1;
    }

    // Which Dunia.dll build this is, and therefore which addresses are valid. Everything FCSE
    // installs below reaches into the engine at addresses that differ between the two shipped v1.03
    // builds, so this has to settle before any of it runs. Refusing here is deliberate: continuing
    // would install hooks at addresses belonging to a different build, and the resulting crash would
    // land far from its cause.
    const BuildInfo build = IdentifyDuniaBuild(DuniaApi::Module());
    if (!build.supported) {
        Log::Loader(std::string("unsupported game build (") + build.id + ") - refusing to start");
        const std::string text =
            build.reason + "\n\nFCSE has not started, and your game has not been modified.";
        MessageBoxA(nullptr, text.c_str(), "FCSE - unsupported Far Cry 2 version",
                    MB_ICONERROR | MB_OK);
        Log::Shutdown();
        return 1;
    }

    if (!AddressLibrary::Init(DuniaApi::Module(), build)) {
        MessageBoxW(nullptr,
                     L"FCSE could not load its address library - see bin\\fcse.log for details.\n\n"
                     L"FCSE has not started, and your game has not been modified.",
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

    // Also FCSE's own, and the earliest-firing thing this loader installs: the splash is built
    // inside RunGame below, before InitDuniaEngine, so this has to be in place before the handoff.
    // See splash.h for why it detours a gdiplus import rather than anything in the game.
    Splash::Install();

    // Before any plugin can register: registration resolves each setting against what this loads,
    // and fires the plugin's callback with the result, so the file has to be in memory first.
    SettingsRegistry::Init(LoaderPaths::In(L"fcse.ini"));

    const std::wstring pluginsDirectory = LoaderPaths::In(L"plugins\\");
    PluginLoader::LoadAll(PluginApi::Build(build), pluginsDirectory);

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
    if (const std::string* frames = SettingsRegistry::RawValue("Tick self check frames")) {
        TickSource::SetSelfCheckTicks(std::atoi(frames->c_str()));
    }
    TickSource::Install();

    // Every plugin has now declared what it has, so the file can be completed in one write:
    // newly-added settings get their defaults, and a first run produces a fully hand-editable
    // fcse.ini without the player ever opening the menu.
    SettingsRegistry::Flush();

    DuniaApi::RegisterGameFunctionProvider()(reinterpret_cast<void*>(&ProvideGameFunctions));

    Log::Loader("handing off to RunGame");
    bool ok = DuniaApi::RunGame()(hInstance, lpCmdLine);
    Log::Loader(std::string("RunGame returned ") + (ok ? "true" : "false"));

    // Before the interpreter closes: a session where the frame hook never fired ends silently
    // otherwise, and that is the one outcome worth saying out loud.
    TickSource::Finish();
    LuaHost::Shutdown();
    HookManager::Shutdown();
    CrashLog::Shutdown();
    Log::Shutdown();
    return ok ? 0 : 1;
}
