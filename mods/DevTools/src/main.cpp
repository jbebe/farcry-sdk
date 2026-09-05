// DevTools - the developer side of Far Cry 2, made reachable.
//
// A plugin for people working on the game rather than playing it: the parts of the engine Ubisoft
// left in the shipped build but closed off, opened again. Like UFCP it patches the running game
// rather than the files on disk, so nothing is overwritten and nothing survives uninstalling it.
//
// One fix per file in src/fixes/, applied unconditionally, and one option per file in src/options/,
// each a settings row FCSE persists in bin\fcse.ini. Adding either: write the file, then declare
// and wire it below.
#include "fcse_api.h"

// -load <save>.sav crashes to desktop instead of launching into the save.
void ApplyLoadSavegameFix();

void __cdecl OnDeveloperConsoleChanged(const FCSE_SettingValue* value, void* userdata);

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // FCSE logs the refusal
    }

    // Wires up the address library and the pattern scanner behind FCSE::Relocation, which is how
    // everything here finds the code it patches - so a failure leaves nothing that could work.
    if (!FCSE::Bind(api)) {
        return false;
    }

    api->Log("DevTools loaded");

    // FCSE_Load runs before any Dunia.dll engine code, so the fix is in place before the game that
    // would read it starts.
    ApplyLoadSavegameFix();

    // The callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so the
    // console is in the state it was left in by the time this returns, and again on every in-game
    // change. There is no separate startup pass.
    static const FCSE_Setting settings[] = {
        {"Developer console", FCSE_CHECKBOX(false), &OnDeveloperConsoleChanged, nullptr},
    };
    api->RegisterSettings("DevTools", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}
