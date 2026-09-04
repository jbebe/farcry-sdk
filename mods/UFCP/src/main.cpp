// UFCP - Unofficial Far Cry Patch.
//
// A collection of fixes for bugs Ubisoft never patched, applied to the running game rather than to
// the files on disk: nothing is overwritten, nothing survives uninstalling the plugin, and it
// coexists with the data mods everyone already has installed.
//
// Two kinds of thing live here, and the split is the whole organising principle:
//
//   src/fixes/    Wrong behaviour, corrected. Applied unconditionally, with no setting, because a
//                 fix that needs a switch is a preference in disguise. Content that the game ships
//                 but can no longer unlock counts as wrong behaviour - see bonus_content.cpp.
//   src/options/  Preferences, where the right answer depends on the player or their hardware.
//                 One settings row each, persisted by FCSE in bin\fcse.ini, and every one of them
//                 defaults to leaving the game exactly as it shipped.
//
// Adding either: write the file, then declare and wire it below.
#include "fcse_api.h"

// Fixes. Each finds its own site and applies itself, or logs why it could not.
void ApplyJackalTapesFix();
void ApplyPredecessorTapesUnlock();
void ApplyMachetesUnlock();
// -load <save>.sav crashes to desktop instead of launching into the save.
void ApplyLoadSavegameFix();
// Quitting from the menu crashes to desktop instead of closing cleanly.
void ApplyExitTeardownFix();

// Options. The hook has to exist before the setting that drives it is registered, because
// registration is what delivers the saved value.
void InstallFovHook();
void __cdecl OnFovChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnAffinityChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnDeveloperConsoleChanged(const FCSE_SettingValue* value, void* userdata);

namespace {
    // Index order is the file format: these labels are what FCSE writes into bin\fcse.ini and reads
    // back, so reordering them changes the meaning of a value a player already saved.
    const char* const kAffinityModes[] = {"All cores", "Physical cores only", "4 cores", "1 core"};
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // FCSE logs the refusal
    }

    // Wires up the address library and the pattern scanner behind FCSE::Relocation, which is how
    // everything here finds the code it patches - so a failure leaves nothing that could work.
    if (!FCSE::Bind(api)) {
        return false;
    }

    api->Log("UFCP loaded");

    // FCSE_Load runs before any Dunia.dll engine code, so every fix is in place before the game
    // that would read it starts.
    ApplyJackalTapesFix();
    ApplyPredecessorTapesUnlock();
    ApplyMachetesUnlock();
    ApplyLoadSavegameFix();
    ApplyExitTeardownFix();

    InstallFovHook();

    // Each callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so both
    // options are in the state the player chose by the time this returns, and again on every
    // in-game change. There is no separate startup pass.
    //
    // The FOV default is the game's own 75, the affinity default is every processor, and the
    // console is left as it shipped, so a fresh install of UFCP applies its fixes and changes
    // nothing else.
    static const FCSE_Setting settings[] = {
        {"Field of view", FCSE_SLIDER(75), &OnFovChanged, nullptr, nullptr, 0, 65, 120},
        {"Processor affinity", FCSE_CHOICE(0), &OnAffinityChanged, nullptr, kAffinityModes, 4},
        {"Developer console", FCSE_CHECKBOX(false), &OnDeveloperConsoleChanged, nullptr},
    };
    api->RegisterSettings("UFCP", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}
