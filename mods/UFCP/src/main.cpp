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
//                 One settings row each, persisted by FCSE in bin\fcse.ini, and defaulting to
//                 leaving the game exactly as it shipped - except for the three named below, which
//                 are what a player installing a patch is asking for.
//
// Adding either: write the file, then declare and wire it below.
#include "fcse_api.h"

#include <iterator>

#include "engine/command_line.h"
#include "engine/input_device.h"

// Fixes. Each finds its own site and applies itself, or logs why it could not.
void ApplyJackalTapesFix();
void ApplyPredecessorTapesUnlock();
void ApplyMachetesUnlock();
// Quitting from the menu crashes to desktop instead of closing cleanly.
void ApplyExitTeardownFix();
// A fast mouse flick turns the view less far than a slow one.
void ApplyMouseSpeedCapFix();
// Loading screens run far below the rate they were paced for.
void ApplyHighPrecisionTimerFix();
// Controllers never rumble, on any pad.
void ApplyVibrationFix();
// The engine leaves processors idle and busy-waits for its own frame cap.
void ApplyUtilisationFix();

// Options. The hook has to exist before the setting that drives it is registered, because
// registration is what delivers the saved value.
void InstallFovHook();
void __cdecl OnFovChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnViewmodelFovChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnIronsightFovChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnVehicleFovChanged(const FCSE_SettingValue* value, void* userdata);

void InstallLookSensitivityHook();
void __cdecl OnMouseSensitivityChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnControllerSensitivityChanged(const FCSE_SettingValue* value, void* userdata);

void InstallAimAssistHook();
void __cdecl OnAimAssistChanged(const FCSE_SettingValue* value, void* userdata);

void InstallInputTogglesHook();
void __cdecl OnAimToggleChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnControllerAimToggleChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnSprintToggleChanged(const FCSE_SettingValue* value, void* userdata);

void InstallSprintTurnHook();
void __cdecl OnSprintTurnChanged(const FCSE_SettingValue* value, void* userdata);

void InstallSkipIntroHook();
void __cdecl OnSkipIntroChanged(const FCSE_SettingValue* value, void* userdata);

void InstallSkipTitleScreenHook();
void __cdecl OnSkipTitleScreenChanged(const FCSE_SettingValue* value, void* userdata);

void InstallMaxFrameRateHook();
void __cdecl OnMaxFrameRateChanged(const FCSE_SettingValue* value, void* userdata);

void __cdecl OnDisplayModeChanged(const FCSE_SettingValue* value, void* userdata);
void __cdecl OnAffinityChanged(const FCSE_SettingValue* value, void* userdata);

namespace {
    // Index order is the file format: these labels are what FCSE writes into bin\fcse.ini and reads
    // back, so reordering them changes the meaning of a value a player already saved.
    const char* const kAffinityModes[] = {"All cores", "Physical cores only", "4 cores", "1 core"};

    // The same applies here, and doubly so: FCSE stores a Choice by its label, so the text is as
    // load-bearing as the order. max_frame_rate.cpp indexes its own table by the same choice.
    const char* const kMaxFrameRates[] = {"Game default", "Unlocked", "Screen refresh", "30 Hz",
                                          "60 Hz",        "72 Hz",    "75 Hz",          "90 Hz",
                                          "120 Hz",       "144 Hz",   "165 Hz",         "180 Hz",
                                          "240 Hz"};

    // The default, and one of the three settings that deliberately do not leave the game as it
    // shipped - see the contract note above.
    constexpr uint32_t kMaxFrameRate60Hz = 4;

    // Fullscreen and windowed are already on the game's own video page, so only what is missing
    // from it is offered here.
    const char* const kDisplayModes[] = {"Game default", "Borderless"};
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
    ApplyExitTeardownFix();
    ApplyMouseSpeedCapFix();
    ApplyHighPrecisionTimerFix();
    ApplyUtilisationFix();

    // Before the fixes and options that ask which device is in the player's hands.
    UFCP::InstallInputDeviceTracking();
    ApplyVibrationFix();

    InstallFovHook();
    InstallLookSensitivityHook();
    InstallAimAssistHook();
    InstallInputTogglesHook();
    InstallSprintTurnHook();
    InstallSkipIntroHook();
    InstallSkipTitleScreenHook();
    InstallMaxFrameRateHook();
    UFCP::InstallRunGameHook();

    // Each callback fires from inside RegisterSettings carrying whatever fcse.ini holds, so every
    // option is in the state the player chose by the time this returns, and again on every in-game
    // change. There is no separate startup pass, which is why the hooks above come first.
    //
    // Every default is the game's own value, with three deliberate exceptions: the intro and title
    // screen are skipped and the frame rate is capped at 60. Those are what someone installing a
    // patch is asking for, and each is one row away from the stock behaviour.
    static const FCSE_Setting settings[] = {
        {"Field of view", FCSE_SLIDER(75), &OnFovChanged, nullptr, nullptr, 0, 65, 120},
        {"Viewmodel field of view", FCSE_SLIDER(75), &OnViewmodelFovChanged, nullptr, nullptr, 0,
         45, 140},
        {"Ironsight field of view", FCSE_SLIDER(0), &OnIronsightFovChanged, nullptr, nullptr, 0, 0,
         140},
        {"Vehicle field of view", FCSE_SLIDER(0), &OnVehicleFovChanged, nullptr, nullptr, 0, 0,
         140},
        // Hundredths, because FCSE has no float setting; 100 is the game's own value.
        {"Mouse look sensitivity", FCSE_SLIDER(100), &OnMouseSensitivityChanged, nullptr, nullptr,
         0, 10, 500},
        {"Controller look sensitivity", FCSE_SLIDER(100), &OnControllerSensitivityChanged, nullptr,
         nullptr, 0, 5, 200},
        {"Controller aim assist", FCSE_CHECKBOX(true), &OnAimAssistChanged},
        {"Aim toggle", FCSE_CHECKBOX(false), &OnAimToggleChanged},
        {"Controller aim toggle", FCSE_CHECKBOX(false), &OnControllerAimToggleChanged},
        {"Sprint toggle", FCSE_CHECKBOX(false), &OnSprintToggleChanged},
        {"Full turn rate while sprinting", FCSE_CHECKBOX(false), &OnSprintTurnChanged},
        {"Skip intro videos", FCSE_CHECKBOX(true), &OnSkipIntroChanged},
        {"Skip title screen", FCSE_CHECKBOX(true), &OnSkipTitleScreenChanged},
        {"Maximum frame rate", FCSE_CHOICE(kMaxFrameRate60Hz), &OnMaxFrameRateChanged, nullptr,
         kMaxFrameRates, std::size(kMaxFrameRates)},
        {"Display mode", FCSE_CHOICE(0), &OnDisplayModeChanged, nullptr, kDisplayModes, std::size(kDisplayModes)},
        {"Processor affinity", FCSE_CHOICE(0), &OnAffinityChanged, nullptr, kAffinityModes, std::size(kAffinityModes)},
    };
    api->RegisterSettings("UFCP", settings, sizeof(settings) / sizeof(settings[0]));

    return true;
}
