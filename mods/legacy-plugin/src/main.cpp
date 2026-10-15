// Legacy Plugin - the Dunia.dll half of community mods' preference tweaks, applied in memory.
//
// Scubrah's Patch, Realism Plus and FC2JackalFix ship these as a patched Dunia.dll or a code hook.
// Their data stays in their own patch.dat; this supplies only the engine side, one settings row per
// tweak under [LegacyPlugin] in bin\fcse.ini, each defaulting to the game as shipped. Fixes belong
// in UFCP, not here.
//
// Adding a tweak: write src/options/<name>.cpp, then declare, install and register it below.
#include "fcse_api.h"

#include <iterator>

// Each tweak's Install<X>() finds its sites and installs any hooks, or logs that it could not.
void InstallBriefingControl();
void __cdecl OnBriefingControlChanged(const FCSE_SettingValue* value, void* userdata);
void InstallRescueKeepsWeapon();
void __cdecl OnRescueKeepsWeaponChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoForcedRescue();
void __cdecl OnNoForcedRescueChanged(const FCSE_SettingValue* value, void* userdata);
void InstallReputationKept();
void __cdecl OnReputationKeptChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoDiamondMessage();
void __cdecl OnNoDiamondMessageChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoBlinkingItems();
void __cdecl OnNoBlinkingItemsChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoSaveIcons();
void __cdecl OnNoSaveIconsChanged(const FCSE_SettingValue* value, void* userdata);
void InstallLimitedSaving();
void __cdecl OnLimitedSavingChanged(const FCSE_SettingValue* value, void* userdata);
void InstallBrownFilter();
void __cdecl OnBrownFilterChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoHitIndicator();
void __cdecl OnNoHitIndicatorChanged(const FCSE_SettingValue* value, void* userdata);
void InstallHudFade();
void __cdecl OnHudFadeChanged(const FCSE_SettingValue* value, void* userdata);
void InstallSkipTutorials();
void __cdecl OnSkipTutorialsChanged(const FCSE_SettingValue* value, void* userdata);
void InstallAimToggle();
void __cdecl OnAimToggleChanged(const FCSE_SettingValue* value, void* userdata);
void InstallSprintToggle();
void __cdecl OnSprintToggleChanged(const FCSE_SettingValue* value, void* userdata);
void InstallSprintTurn();
void __cdecl OnSprintTurnChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoRimLight();
void __cdecl OnNoRimLightChanged(const FCSE_SettingValue* value, void* userdata);
void InstallNoBlur();
void __cdecl OnNoBlurChanged(const FCSE_SettingValue* value, void* userdata);

namespace {
    // FCSE stores a Choice by its label, so labels and order are the ini format. hud_fade.cpp and
    // skip_tutorials.cpp index their own tables by the same choice.
    const char* const kHudFadeChoices[] = {"Always hidden", "1 s",  "2 s",  "3 s (game default)",
                                           "5 s",           "8 s",  "10 s", "15 s",
                                           "20 s",          "30 s", "60 s", "Always shown"};
    constexpr uint32_t kHudFadeGameDefault = 3;

    const char* const kSkipTutorialsChoices[] = {"Off", "Pop-ups", "Pop-ups and hints"};
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION) {
        return false; // FCSE logs the refusal
    }

    if (!FCSE::Bind(api)) {
        return false;
    }

    api->Log("Legacy Plugin loaded");

    // Before RegisterSettings, which delivers each saved value to its callback.
    InstallBriefingControl();
    InstallRescueKeepsWeapon();
    InstallNoForcedRescue();
    InstallReputationKept();
    InstallNoDiamondMessage();
    InstallNoBlinkingItems();
    InstallNoSaveIcons();
    InstallLimitedSaving();
    InstallBrownFilter();
    InstallNoHitIndicator();
    InstallHudFade();
    InstallSkipTutorials();
    InstallAimToggle();
    InstallSprintToggle();
    InstallSprintTurn();
    InstallNoRimLight();
    InstallNoBlur();

    // Every default is the game as shipped; Brown filter is on because the game ships with it.
    static const FCSE_Setting settings[] = {
        {"Briefings keep control", FCSE_CHECKBOX(false), &OnBriefingControlChanged, nullptr},
        {"Keep weapon on rescue", FCSE_CHECKBOX(false), &OnRescueKeepsWeaponChanged, nullptr},
        {"No forced buddy rescues", FCSE_CHECKBOX(false), &OnNoForcedRescueChanged, nullptr},
        {"No reputation drop", FCSE_CHECKBOX(false), &OnReputationKeptChanged, nullptr},
        {"Hide diamond message", FCSE_CHECKBOX(false), &OnNoDiamondMessageChanged, nullptr},
        {"No blinking pickups", FCSE_CHECKBOX(false), &OnNoBlinkingItemsChanged, nullptr},
        {"No save icons on map", FCSE_CHECKBOX(false), &OnNoSaveIconsChanged, nullptr},
        {"Limited saving", FCSE_CHECKBOX(false), &OnLimitedSavingChanged, nullptr},
        {"Brown filter", FCSE_CHECKBOX(true), &OnBrownFilterChanged, nullptr},
        {"No hit indicator", FCSE_CHECKBOX(false), &OnNoHitIndicatorChanged, nullptr},
        {"HUD fade-out", FCSE_CHOICE(kHudFadeGameDefault), &OnHudFadeChanged, nullptr, kHudFadeChoices,
         std::size(kHudFadeChoices)},
        {"Skip tutorial pop-ups", FCSE_CHOICE(0), &OnSkipTutorialsChanged, nullptr, kSkipTutorialsChoices,
         std::size(kSkipTutorialsChoices)},
        {"Aim toggle", FCSE_CHECKBOX(false), &OnAimToggleChanged, nullptr},
        {"Sprint toggle", FCSE_CHECKBOX(false), &OnSprintToggleChanged, nullptr},
        {"Sprint turn", FCSE_CHECKBOX(false), &OnSprintTurnChanged, nullptr},
        {"No rim lighting", FCSE_CHECKBOX(false), &OnNoRimLightChanged, nullptr},
        {"No sprint and aim blur", FCSE_CHECKBOX(false), &OnNoBlurChanged, nullptr},
    };
    api->RegisterSettings("LegacyPlugin", settings, std::size(settings));

    return true;
}
