// Difficulty Overhaul - realistic damage and AI shooting, scaled by the game's own difficulty.
#include "fcse_api.h"

#include <iterator>

void InstallHitLog();
void __cdecl OnHitLogChanged(const FCSE_SettingValue* value, void* userdata);

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    if (api->apiVersion != FCSE_API_VERSION || !FCSE::Bind(api)) {
        return false;
    }

    // Before RegisterSettings, which delivers each saved value to its callback.
    InstallHitLog();

    static const FCSE_Setting settings[] = {
        {"Hit log", FCSE_CHECKBOX(true), &OnHitLogChanged, nullptr},
    };
    api->RegisterSettings("DifficultyOverhaul", settings, std::size(settings));
    return true;
}
